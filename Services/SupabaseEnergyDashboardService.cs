using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NestStats2.Models;

namespace NestStats2.Services;

public sealed class SupabaseEnergyDashboardService : IEnergyDashboardService
{
    private const int DefaultRelayChannelCount = 8;
    private const int MaxRelayChannelCount = 9;
    private const double DefaultRelayCapacityKw = 2.0;
    private const int SupabasePageSize = 1000;
    private const int StatsHistoryDays = 45;
    private const int StatsMaxRows = 50000;
    private const int EstimatedHistoryRowsPerDay = 1800;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(14);
    private static readonly Regex RelayCountAndPowerRegex = new(
        @"(?<count>\d+)\s*x\s*(?<power>\d+(?:[.,]\d+)?)\s*k\s*w\s*SSR",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TotalPowerRegex = new(
        @"(?<total>\d+(?:[.,]\d+)?)\s*k\s*w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<SystemInfo>>> SystemsCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CacheEntry<LiveSnapshot?>> LiveSnapshotCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SystemsCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LiveSnapshotCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PvChannelMergeWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DailyStatsPreviousDayCutoff = TimeSpan.FromHours(4);
    // Legacy telemetry rows are stored one hour ahead because of server time skew.
    // We query the shifted timestamps from DB, then normalize them back inside the app.
    private static readonly TimeSpan DatabaseTimestampOffset = TimeSpan.FromHours(1);
    private static readonly TimeZoneInfo DashboardTimeZone = ResolveDashboardTimeZone();

    private readonly HttpClient _httpClient;
    private readonly DashboardCatalogOptions _catalog;
    private readonly ILogger<SupabaseEnergyDashboardService> _logger;
    private readonly ISpotMarketPriceService _spotMarketPriceService;
    private readonly TimeZoneInfo _timeZone;
    private readonly bool _isConfigured;

    public SupabaseEnergyDashboardService(
        HttpClient httpClient,
        IOptions<SupabaseOptions> options,
        IOptions<DashboardCatalogOptions> catalogOptions,
        ISpotMarketPriceService spotMarketPriceService,
        ILogger<SupabaseEnergyDashboardService> logger)
    {
        _httpClient = httpClient;
        _catalog = catalogOptions.Value ?? new DashboardCatalogOptions();
        _logger = logger;
        _spotMarketPriceService = spotMarketPriceService;
        _timeZone = DashboardTimeZone;

        var settings = options.Value;
        _isConfigured = !string.IsNullOrWhiteSpace(settings.Url) && !string.IsNullOrWhiteSpace(settings.AnonKey);
        if (!_isConfigured)
        {
            _logger.LogWarning("Supabase configuration is missing. Dashboard telemetry will run in empty offline mode until Supabase:Url and Supabase:AnonKey are configured.");
            return;
        }

        _httpClient.BaseAddress = new Uri($"{settings.Url.TrimEnd('/')}/rest/v1/");
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("apikey", settings.AnonKey);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AnonKey);
    }

    public async Task<IReadOnlyList<SystemInfo>> GetSystemsAsync(CancellationToken cancellationToken = default)
    {
        return await GetSystemsInternalAsync(cancellationToken);
    }

    public async Task<bool> SystemExistsAsync(string snNumber, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || string.IsNullOrWhiteSpace(snNumber))
        {
            return false;
        }

        var encodedSn = Uri.EscapeDataString(snNumber.Trim());
        var systems = await TryGetListAsync<SystemInfo>(
            $"SYSTEM?select=sn_number,system_name,system_address,pocet_ssr,popis&sn_number=eq.{encodedSn}&limit=1",
            cancellationToken);

        return systems.Count > 0;
    }

    public async Task<DashboardData> GetDashboardAsync(
        string? snNumber,
        DateTime? day,
        int hoursBack,
        IReadOnlyCollection<string>? allowedSnNumbers = null,
        IProgress<DashboardLoadProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        static void Report(IProgress<DashboardLoadProgressUpdate>? target, int percent, string stage, string detail)
            => target?.Report(new DashboardLoadProgressUpdate(percent, stage, detail));

        var normalizedHoursBack = NormalizeHoursBack(hoursBack);
        Report(progress, 8, "System", "Nacitam zoznam systemov.");
        var systems = await GetSystemsInternalAsync(cancellationToken);

        if (allowedSnNumbers is not null)
        {
            var allowed = new HashSet<string>(allowedSnNumbers.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            systems = systems.Where(x => allowed.Contains(x.sn_number)).ToList();
        }

        if (systems.Count == 0)
        {
            var now = DateTime.UtcNow;
            return new DashboardData
            {
                SelectedDay = day,
                HoursBack = normalizedHoursBack,
                SystemProfile = ResolveSystemProfile(null),
                InstalledPvKw = _catalog.DefaultSystemProfile.InstalledPvKw,
                BatteryCapacityKwh = _catalog.DefaultSystemProfile.BatteryCapacityKwh,
                WattMaxKw = _catalog.DefaultSystemProfile.WattMaxKw,
                RelayChannelCount = DefaultRelayChannelCount,
                RangeStartLocal = TimeZoneInfo.ConvertTimeFromUtc(now.AddHours(-normalizedHoursBack), _timeZone),
                RangeEndLocal = TimeZoneInfo.ConvertTimeFromUtc(now, _timeZone)
            };
        }

        Report(progress, 15, "System", "Vyberam cielovy system a casove okno.");
        var selectedSn = string.IsNullOrWhiteSpace(snNumber)
            ? await ResolveDefaultSystemSnAsync(systems, cancellationToken)
            : snNumber.Trim();
        if (systems.All(x => !string.Equals(x.sn_number, selectedSn, StringComparison.OrdinalIgnoreCase)))
        {
            selectedSn = await ResolveDefaultSystemSnAsync(systems, cancellationToken);
        }

        var selectedSystem = systems.FirstOrDefault(x => string.Equals(x.sn_number, selectedSn, StringComparison.OrdinalIgnoreCase)) ?? systems[0];
        selectedSn = selectedSystem.sn_number;
        var systemProfile = ResolveSystemProfile(selectedSn);
        var relayChannelCount = ResolveRelayChannelCount(selectedSystem.pocet_ssr);
        var relayCapacityKw = ResolveRelayCapacityKw(systemProfile, selectedSystem, relayChannelCount);

        Report(progress, 20, "Okno", "Zistujem referencny cas telemetrie.");
        var referenceEndUtc = !day.HasValue
            ? await ResolveTelemetryReferenceTimeAsync(selectedSn, cancellationToken)
            : (DateTimeOffset?)null;

        var (fromUtc, toUtc) = BuildWindow(day, normalizedHoursBack, referenceEndUtc);
        var referenceLocalDate = day?.Date ?? ToLocalAppTime(DateTimeOffset.UtcNow).DateTime.Date;
        var statsWindow = BuildStatsDayWindow(referenceLocalDate);
        var statsFromUtc = statsWindow.FromUtc;
        var statsToUtc = statsWindow.ToUtc;
        var statsOrder = "asc";

        var encodedSn = Uri.EscapeDataString(selectedSn);
        var fromStr = Uri.EscapeDataString(ToDatabaseTimestamp(fromUtc).ToString("O"));
        var toStr = Uri.EscapeDataString(ToDatabaseTimestamp(toUtc).ToString("O"));
        var statsFromStr = Uri.EscapeDataString(ToDatabaseTimestamp(statsFromUtc).ToString("O"));
        var statsToFilter = $"&timestamp=lt.{Uri.EscapeDataString(ToDatabaseTimestamp(statsToUtc).ToString("O"))}";

        var wattUrl = $"WATTROUTER_INFO?sn_number=eq.{encodedSn}&created_at=gte.{fromStr}&created_at=lt.{toStr}&order=created_at.asc&limit=4000";
        var pvUrl = $"PV_INFORMATION?sn_number=eq.{encodedSn}&created_at=gte.{fromStr}&created_at=lt.{toStr}&order=created_at.asc&limit=4000";
        var batteryUrl = $"BATTERY_INFORMATION?sn_number=eq.{encodedSn}&created_at=gte.{fromStr}&created_at=lt.{toStr}&order=created_at.asc&limit=4000";
        var gridUrl = $"GRID_INFORMATION?sn_number=eq.{encodedSn}&created_at=gte.{fromStr}&created_at=lt.{toStr}&order=created_at.asc&limit=4000";
        var statsUrl = $"STATISTICAL_INFORMATION?sn_number=eq.{encodedSn}&timestamp=gte.{statsFromStr}{statsToFilter}&order=timestamp.{statsOrder}";

        var wattTask = RunDashboardQueryAsync("WattRouter", token => TryGetListAsync<WattRouterInfo>(wattUrl, token), cancellationToken);
        var pvTask = RunDashboardQueryAsync("FV stringy", token => TryGetListAsync<PvInformation>(pvUrl, token), cancellationToken);
        var batteryTask = RunDashboardQueryAsync("Bateria", token => TryGetListAsync<BatteryInformation>(batteryUrl, token), cancellationToken);
        var gridTask = RunDashboardQueryAsync("Siet", token => TryGetListAsync<GridInformation>(gridUrl, token), cancellationToken);
        var statsTask = RunDashboardQueryAsync(
            "Statistika",
            token => TryGetPagedListAsync<StatisticalInformation>(statsUrl, Math.Max(SupabasePageSize, EstimatedHistoryRowsPerDay), token),
            cancellationToken);
        Report(progress, 25, "Databaza", "Spustam paralelne nacitanie dat zo vsetkych zdrojov.");

        var taskLabels = new Dictionary<Task, string>
        {
            [wattTask] = "WattRouter",
            [pvTask] = "FV stringy",
            [batteryTask] = "Bateria",
            [gridTask] = "Siet",
            [statsTask] = "Statistika"
        };

        var completedTasks = 0;
        var totalDataTasks = taskLabels.Count;
        while (taskLabels.Count > 0)
        {
            var finishedTask = await Task.WhenAny(taskLabels.Keys);
            completedTasks += 1;
            var label = taskLabels[finishedTask];
            taskLabels.Remove(finishedTask);
            var percent = 25 + (int)Math.Round(completedTasks * 40d / Math.Max(1, totalDataTasks));
            Report(progress, percent, "Databaza", $"Nacitane: {label} ({completedTasks}/{totalDataTasks}).");
        }

        var wattPoints = wattTask.Result;
        var wattPowerSeries = wattPoints.Select(x => ComputeWattPowerKw(x, relayCapacityKw)).ToArray();
        var pvPoints = pvTask.Result;
        var batteryPoints = batteryTask.Result;
        var gridPoints = gridTask.Result;
        var rawStats = statsTask.Result;

        Report(progress, 68, "Live", "Doplnam live snapshot a synchronizujem timeline.");
        var dailyStats = BuildDailyStatsSegments(rawStats);
        var metricHistory = BuildHistory(dailyStats);
        var history = metricHistory;
        var totalHistory = BuildTotalHistory(dailyStats);
        var latestStats = ResolveLatestStats(dailyStats, rawStats, day);
        var latestOverallStats = rawStats.MaxBy(x => x.timestamp);
        var initialLive = await GetLiveSnapshotInternalAsync(selectedSn, systemProfile, relayChannelCount, relayCapacityKw, cancellationToken);

        var latestWatt = wattPoints.LastOrDefault();
        var latestGrid = gridPoints.LastOrDefault();
        var latestBattery = batteryPoints.LastOrDefault();

        var latestPvKw = initialLive?.pvTotal ?? SumLatestPvGroup(pvPoints);
        var latestGridKw = initialLive?.gridPower ?? latestGrid?.active_power_pcc_total ?? 0;
        var latestInverterKw = initialLive?.inverterPower ?? latestGrid?.active_power_output_total ?? 0;
        var latestBatteryKw = initialLive?.batteryPower ?? latestBattery?.power ?? 0;
        var latestWattKw = initialLive?.wattPowerKw ?? wattPowerSeries.LastOrDefault();
        var latestConsumptionKw = initialLive?.consumption ?? ComputeConsumptionKw(latestGridKw, latestInverterKw, latestPvKw, latestBatteryKw);
        var currentPvVoltage = initialLive?.pvVoltage ?? 0;
        var currentPvCurrent = initialLive?.pvCurrent ?? 0;
        var currentBatteryVoltage = initialLive?.batteryVoltage ?? latestBattery?.voltage ?? 0;
        var currentBatteryCurrent = initialLive?.batteryCurrent ?? latestBattery?.current ?? 0;
        var currentGridFrequency = initialLive?.gridFrequency ?? latestGrid?.grid_frequency ?? 0;
        var currentMpptImbalancePct = ComputeMpptImbalancePct(initialLive?.mppt1Power ?? 0, initialLive?.mppt2Power ?? 0);
        Report(progress, 78, "Grafy", "Skladam timeline, historiu a agregacie grafov.");
        var charts = BuildCharts(wattPoints, wattPowerSeries, pvPoints, batteryPoints, gridPoints, history, totalHistory, systemProfile, relayChannelCount);
        var timeline = charts.Timeline;

        var localNow = ToLocalAppTime(DateTimeOffset.UtcNow).DateTime;
        var selectedHistoryPoint = day.HasValue
            ? metricHistory.FirstOrDefault(x => string.Equals(x.Date, day.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            : metricHistory.LastOrDefault();
        selectedHistoryPoint ??= metricHistory.LastOrDefault() ?? history.LastOrDefault();

        var todayPv = selectedHistoryPoint?.Pv ?? 0;
        var todayConsumption = selectedHistoryPoint?.Consumption ?? 0;
        var todayImport = selectedHistoryPoint?.Import ?? 0;
        var todayExport = selectedHistoryPoint?.Export ?? 0;
        var todayBatteryCharge = selectedHistoryPoint?.BatteryCharge ?? 0;
        var todayBatteryDischarge = selectedHistoryPoint?.BatteryDischarge ?? 0;
        var todaySelfUse = selectedHistoryPoint?.SelfUseKwh ?? Math.Max(0, todayPv - todayExport);
        var selfConsumptionPct = selectedHistoryPoint?.SelfUsePct ?? (todayPv > 0 ? Math.Round(todaySelfUse / todayPv * 100, 1) : 0);
        var selfSufficiencyKwh = selectedHistoryPoint?.SelfSufficiencyKwh ?? Math.Max(0, todayConsumption - todayImport);
        var selfSufficiencyPct = selectedHistoryPoint?.SelfSufficiencyPct ?? (todayConsumption > 0 ? Math.Round(selfSufficiencyKwh / todayConsumption * 100, 1) : 0);
        var gridDependencyPct = todayConsumption > 0 ? Math.Round(todayImport / todayConsumption * 100, 1) : 0;
        var exportLossPct = todayPv > 0 ? Math.Round(todayExport / todayPv * 100, 1) : 0;
        var fullLoadHoursToday = systemProfile.InstalledPvKw > 0 ? Math.Round(todayPv / systemProfile.InstalledPvKw, 2) : 0;

        var avgWattKw = wattPowerSeries.Length > 0 ? Math.Round(wattPowerSeries.Average(), 2) : 0;
        var maxWattKw = timeline.Count > 0 ? Math.Round(timeline.Max(x => x.WattKw), 2) : Math.Round(wattPowerSeries.DefaultIfEmpty().Max(), 2);
        var relayOnPercentage = wattPoints.Count > 0
            ? Math.Round(wattPoints.Count(x => x.ActiveChannelCount > 0) / (double)wattPoints.Count * 100, 1)
            : 0;
        var totalWattEnergy = ComputeEnergyFromSeries(wattPoints.Select(x => x.created_at).ToArray(), wattPowerSeries);
        var dailyWattCapturePct = Math.Round((totalWattEnergy + todayExport) > 0 ? totalWattEnergy / (totalWattEnergy + todayExport) * 100 : 0, 1);
        var averagePvKw = timeline.Count > 0 ? Math.Round(timeline.Average(x => x.PvKw), 2) : 0;
        var peakPvKw = timeline.Count > 0 ? Math.Round(timeline.Max(x => x.PvKw), 2) : 0;
        var peakConsumptionKw = timeline.Count > 0 ? Math.Round(timeline.Max(x => x.ConsumptionKw), 2) : 0;
        var baseLoadKw = ComputeBaseLoadKw(timeline);
        var peakImportKw = timeline.Count > 0 ? Math.Round(timeline.Where(x => x.GridKw < 0).Select(x => Math.Abs(x.GridKw)).DefaultIfEmpty().Max(), 2) : 0;
        var peakExportKw = timeline.Count > 0 ? Math.Round(timeline.Where(x => x.GridKw > 0).Select(x => x.GridKw).DefaultIfEmpty().Max(), 2) : 0;
        var importWindowPct = timeline.Count > 0 ? Math.Round(timeline.Count(x => x.GridKw < -0.05) / (double)timeline.Count * 100, 1) : 0;
        var exportWindowPct = timeline.Count > 0 ? Math.Round(timeline.Count(x => x.GridKw > 0.05) / (double)timeline.Count * 100, 1) : 0;
        var pvSaturationPct = systemProfile.InstalledPvKw > 0 ? Math.Round(peakPvKw / systemProfile.InstalledPvKw * 100, 1) : 0;
        var currentPvSaturationPct = systemProfile.InstalledPvKw > 0 ? Math.Round(latestPvKw / systemProfile.InstalledPvKw * 100, 1) : 0;
        var currentWattUtilizationPct = ComputeWattUtilizationPct(latestWatt, relayChannelCount, initialLive?.wattUtilizationPct);
        var currentRelayAverageLoadPct = Math.Round(latestWatt?.GetAverageChannelLoadPercentage(relayChannelCount) ?? initialLive?.relayAverageLoadPct ?? 0, 1);
        var batteryAutonomy = latestConsumptionKw > 0.05 && (latestBattery?.soc ?? 0) > 0
            ? Math.Round(((latestBattery?.soc ?? 0) / 100.0) * systemProfile.BatteryCapacityKwh / latestConsumptionKw, 1)
            : 0;
        var batteryThroughputToday = Math.Round(todayBatteryCharge + todayBatteryDischarge, 2);
        var batteryCycleToday = systemProfile.BatteryCapacityKwh > 0
            ? Math.Round(batteryThroughputToday / (systemProfile.BatteryCapacityKwh * 2d), 2)
            : 0;
        var batterySocSamples = batteryPoints
            .Where(x => x.soc.HasValue)
            .Select(x => (double)x.soc!.Value)
            .Where(x => x is >= 0 and <= 100)
            .ToArray();
        var minBatterySoc = batterySocSamples.Length > 0
            ? Math.Round(batterySocSamples.Min(), 1)
            : Math.Round((double)(latestBattery?.soc ?? 0), 1);
        var averageBatterySoc = batterySocSamples.Length > 0
            ? Math.Round(batterySocSamples.Average(), 1)
            : Math.Round((double)(latestBattery?.soc ?? 0), 1);
        var maxBatterySoc = batterySocSamples.Length > 0
            ? Math.Round(batterySocSamples.Max(), 1)
            : Math.Round((double)(latestBattery?.soc ?? 0), 1);
        var lifetimeBatteryCharge = latestOverallStats?.battery_charge_total ?? 0;
        var lifetimeBatteryDischarge = latestOverallStats?.battery_discharge_total ?? 0;
        var batteryRoundtripProxyPct = lifetimeBatteryCharge > 0
            ? Math.Round(Math.Clamp(lifetimeBatteryDischarge / lifetimeBatteryCharge * 100, 0, 100), 1)
            : 0;
        var averageBatteryTempC = batteryPoints.Count > 0
            ? Math.Round(batteryPoints.Where(x => x.temperature.HasValue).Select(x => (double)x.temperature!.Value).DefaultIfEmpty().Average(), 1)
            : 0;
        var peakBatteryTempC = batteryPoints.Count > 0
            ? Math.Round(batteryPoints.Where(x => x.temperature.HasValue).Select(x => (double)x.temperature!.Value).DefaultIfEmpty().Max(), 1)
            : Math.Round(latestBattery?.temperature ?? 0, 1);
        var averageGridFrequencyHz = gridPoints.Count > 0
            ? Math.Round(gridPoints.Where(x => x.grid_frequency.HasValue).Select(x => x.grid_frequency!.Value).DefaultIfEmpty().Average(), 2)
            : 0;
        var freshnessMinutes = initialLive != null
            ? Math.Max(0, Math.Round((DateTimeOffset.UtcNow - initialLive.time).TotalMinutes, 1))
            : 0;
        var lifetimePv = latestOverallStats?.pv_generation_total ?? 0;
        var lifetimeConsumption = latestOverallStats?.consumption_total ?? 0;
        var lifetimeImport = latestOverallStats?.purchase_total ?? 0;
        var lifetimeExport = latestOverallStats?.sell_total ?? 0;
        var lifetimeSelfUse = Math.Max(0, lifetimePv - lifetimeExport);
        var lifetimeSelfSufficiency = Math.Max(0, lifetimeConsumption - lifetimeImport);
        var projectedMonthlyPv = ProjectMonthly(history.Select(x => x.Pv), todayPv);
        var projectedAnnualPv = Math.Round(projectedMonthlyPv * 12, 1);
        var projectedMonthlySelfSufficiency = ProjectMonthly(history.Select(x => x.SelfSufficiencyKwh), selfSufficiencyKwh);
        var projectedAnnualSelfSufficiency = Math.Round(projectedMonthlySelfSufficiency * 12, 1);
        var projectedMonthlyExport = ProjectMonthly(history.Select(x => x.Export), todayExport);
        var projectedAnnualExport = Math.Round(projectedMonthlyExport * 12, 1);

        Report(progress, 88, "Analyza", "Pocitam KPI, odporucania a ekonomiku.");
        var tariffBenchmarks = BuildTariffBenchmarks(selfSufficiencyKwh, projectedMonthlySelfSufficiency, projectedAnnualSelfSufficiency, lifetimeSelfSufficiency);
        var exportRevenue = BuildExportRevenue(todayExport, projectedMonthlyExport, projectedAnnualExport, lifetimeExport);
        var environmentalBenefits = BuildEnvironmentalBenefits(todayPv, projectedMonthlyPv, projectedAnnualPv, lifetimePv, tariffBenchmarks, exportRevenue);
        var energyScore = ComputeEnergyScore(selfConsumptionPct, selfSufficiencyPct, exportLossPct, freshnessMinutes, latestBattery?.soc ?? 0, currentPvSaturationPct, currentWattUtilizationPct);

        var statusPills = BuildStatusPills(initialLive, latestBattery, freshnessMinutes, selfSufficiencyPct, gridDependencyPct, currentPvSaturationPct, currentWattUtilizationPct);
        var deviceStates = BuildDeviceStates(latestPvKw, latestInverterKw, latestConsumptionKw, latestGridKw, latestBatteryKw, latestWattKw, latestBattery, currentPvSaturationPct, currentWattUtilizationPct, currentRelayAverageLoadPct, latestWatt);
        var relayStates = latestWatt?.ToRelayStates(relayCapacityKw, relayChannelCount) ?? initialLive?.relayStates ?? [];
        var insights = BuildInsights(todayPv, todayConsumption, todayImport, todayExport, selfConsumptionPct, selfSufficiencyPct, latestBattery?.soc ?? 0, latestGridKw, latestWattKw, freshnessMinutes);
        var smartForecast = BuildSmartForecast(history, todayPv, todayConsumption, todayImport, todayExport);
        var anomalies = BuildAnomalies(timeline, history, latestBattery, freshnessMinutes, currentMpptImbalancePct, averageGridFrequencyHz, peakBatteryTempC, importWindowPct, exportWindowPct);
        var operatorRecommendations = BuildOperatorRecommendations(smartForecast, anomalies, todayExport, todayImport, selfConsumptionPct, selfSufficiencyPct, latestBattery?.soc ?? 0, currentWattUtilizationPct, dailyWattCapturePct);
        var energyBreakdowns = BuildEnergyBreakdowns(todayPv, todayConsumption, todayExport, todayImport, todayBatteryCharge, todayBatteryDischarge, lifetimeSelfUse, lifetimeSelfSufficiency, lifetimeImport, lifetimeExport);
        var spotMarket = await _spotMarketPriceService.GetSkDayAheadSummaryAsync(localNow, cancellationToken);
        var dailyStories = BuildDailyStories(history, localNow.Date, todayPv, todayConsumption, todayImport, todayExport, selfConsumptionPct, selfSufficiencyPct);
        Report(progress, 96, "Finalizacia", "Dokoncujem dashboard a pripravujem odpoved.");

        return new DashboardData
        {
            Systems = systems,
            SelectedSnNumber = selectedSn,
            SystemName = string.IsNullOrWhiteSpace(selectedSystem.system_name) ? selectedSn : selectedSystem.system_name,
            SystemAddress = string.IsNullOrWhiteSpace(selectedSystem.system_address) ? "Lokalita nie je zadaná" : selectedSystem.system_address!,
                RangeStartUtc = fromUtc,
                RangeEndUtc = toUtc,
                RangeStartLocal = ToDashboardDisplayTime(fromUtc).DateTime,
                RangeEndLocal = ToDashboardDisplayTime(toUtc).DateTime,
            SelectedDay = day?.Date,
            HoursBack = normalizedHoursBack,
            SystemProfile = systemProfile,
            InstalledPvKw = systemProfile.InstalledPvKw,
            BatteryCapacityKwh = systemProfile.BatteryCapacityKwh,
            WattMaxKw = systemProfile.WattMaxKw,
            RelayChannelCount = relayChannelCount,
            WattPoints = wattPoints,
            WattPowerKwSeries = wattPowerSeries,
            PvPoints = pvPoints,
            BatteryPoints = batteryPoints,
            GridPoints = gridPoints,
            History = history,
            LatestWatt = latestWatt,
            LatestGrid = latestGrid,
            LatestBattery = latestBattery,
            LatestStats = latestStats,
            InitialLive = initialLive,
            LatestPvKw = Math.Round(latestPvKw, 2),
            LatestConsumptionKw = Math.Round(latestConsumptionKw, 2),
            LatestGridKw = Math.Round(latestGridKw, 2),
            LatestBatteryKw = Math.Round(latestBatteryKw, 2),
            LatestInverterKw = Math.Round(latestInverterKw, 2),
            LatestWattKw = Math.Round(latestWattKw, 2),
            LatestBatterySoC = latestBattery?.soc ?? 0,
            LatestBatteryTempC = Math.Round(latestBattery?.temperature ?? 0, 1),
            CurrentPvVoltageV = Math.Round(currentPvVoltage, 1),
            CurrentPvCurrentA = Math.Round(currentPvCurrent, 1),
            CurrentBatteryVoltageV = Math.Round(currentBatteryVoltage, 1),
            CurrentBatteryCurrentA = Math.Round(currentBatteryCurrent, 1),
            CurrentGridFrequencyHz = Math.Round(currentGridFrequency, 2),
            CurrentMpptImbalancePct = Math.Round(currentMpptImbalancePct, 1),
            CurrentPvSaturationPct = currentPvSaturationPct,
            CurrentWattUtilizationPct = currentWattUtilizationPct,
            CurrentRelayAverageLoadPct = currentRelayAverageLoadPct,
            TodayPv = Math.Round(todayPv, 1),
            TodayConsumption = Math.Round(todayConsumption, 1),
            TodayImport = Math.Round(todayImport, 1),
            TodayExport = Math.Round(todayExport, 1),
            TodayBatteryCharge = Math.Round(todayBatteryCharge, 1),
            TodayBatteryDischarge = Math.Round(todayBatteryDischarge, 1),
            TodaySelfUseKwh = Math.Round(todaySelfUse, 1),
            SelfConsumptionPct = selfConsumptionPct,
            SelfSufficiencyPct = selfSufficiencyPct,
            GridDependencyPct = gridDependencyPct,
            ExportLossPct = exportLossPct,
            FullLoadHoursToday = fullLoadHoursToday,
            SpecificYieldToday = fullLoadHoursToday,
            AvgWattKw = avgWattKw,
            MaxWattKw = maxWattKw,
            RelayOnPercentage = relayOnPercentage,
            TotalWattEnergyKwh = totalWattEnergy,
            AveragePvKw = averagePvKw,
            PeakPvKw = peakPvKw,
            PeakConsumptionKw = peakConsumptionKw,
            BaseLoadKw = baseLoadKw,
            PeakImportKw = peakImportKw,
            PeakExportKw = peakExportKw,
            PvSaturationPct = pvSaturationPct,
            BatteryAutonomyHours = batteryAutonomy,
            BatteryThroughputTodayKwh = batteryThroughputToday,
            BatteryCycleToday = batteryCycleToday,
            MinBatterySoc = minBatterySoc,
            AverageBatterySoc = averageBatterySoc,
            MaxBatterySoc = maxBatterySoc,
            BatteryRoundtripProxyPct = batteryRoundtripProxyPct,
            DailyWattCapturePct = dailyWattCapturePct,
            ImportWindowPct = importWindowPct,
            ExportWindowPct = exportWindowPct,
            AverageBatteryTempC = averageBatteryTempC,
            PeakBatteryTempC = peakBatteryTempC,
            AverageGridFrequencyHz = averageGridFrequencyHz,
            EnergyScore = energyScore,
            DataFreshnessMinutes = freshnessMinutes,
            LifetimePv = Math.Round(lifetimePv, 1),
            LifetimeConsumption = Math.Round(lifetimeConsumption, 1),
            LifetimeImport = Math.Round(lifetimeImport, 1),
            LifetimeExport = Math.Round(lifetimeExport, 1),
            LifetimeBatteryCharge = Math.Round(lifetimeBatteryCharge, 1),
            LifetimeBatteryDischarge = Math.Round(lifetimeBatteryDischarge, 1),
            LifetimeSelfUseKwh = Math.Round(lifetimeSelfUse, 1),
            LifetimeSelfSufficiencyKwh = Math.Round(lifetimeSelfSufficiency, 1),
            ProjectedMonthlyPvKwh = projectedMonthlyPv,
            ProjectedAnnualPvKwh = projectedAnnualPv,
            ProjectedMonthlySelfSufficiencyKwh = projectedMonthlySelfSufficiency,
            ProjectedAnnualSelfSufficiencyKwh = projectedAnnualSelfSufficiency,
            ProjectedMonthlyExportKwh = projectedMonthlyExport,
            ProjectedAnnualExportKwh = projectedAnnualExport,
            RelayStates = relayStates,
            StatusPills = statusPills,
            DeviceStates = deviceStates,
            Insights = insights,
            TariffBenchmarks = tariffBenchmarks,
            EnergyBreakdowns = energyBreakdowns,
            SmartForecast = smartForecast,
            Anomalies = anomalies,
            OperatorRecommendations = operatorRecommendations,
            ExportRevenue = exportRevenue,
            EnvironmentalBenefits = environmentalBenefits,
            Charts = charts,
            SpotMarket = spotMarket,
            DailyStories = dailyStories
        };
    }

    public async Task<IReadOnlyList<DailyHistoryPoint>> GetHistoryAsync(
        string snNumber,
        DateTime? referenceDay,
        int? days,
        IReadOnlyCollection<string>? allowedSnNumbers = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || string.IsNullOrWhiteSpace(snNumber))
        {
            return [];
        }

        if (allowedSnNumbers is not null)
        {
            var allowed = new HashSet<string>(
                allowedSnNumbers.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            if (!allowed.Contains(snNumber))
            {
                return [];
            }
        }

        var normalizedDays = days.HasValue
            ? Math.Clamp(days.Value, 1, StatsHistoryDays)
            : (int?)null;

        var rows = await GetHistoryStatsRowsAsync(snNumber, referenceDay, normalizedDays, cancellationToken);
        var history = BuildHistory(BuildDailyStatsSegments(rows));

        return normalizedDays.HasValue
            ? history.TakeLast(normalizedDays.Value).ToArray()
            : history;
    }

    public async Task<LiveSnapshot?> GetLiveSnapshotAsync(string snNumber, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return null;
        }

        var profile = ResolveSystemProfile(snNumber);
        var systems = await GetSystemsInternalAsync(cancellationToken);
        var system = systems.FirstOrDefault(x => string.Equals(x.sn_number, snNumber, StringComparison.OrdinalIgnoreCase));
        var relayChannelCount = ResolveRelayChannelCount(system?.pocet_ssr);
        return await GetLiveSnapshotInternalAsync(
            snNumber,
            profile,
            relayChannelCount,
            ResolveRelayCapacityKw(profile, system, relayChannelCount),
            cancellationToken);
    }

    private async Task<List<SystemInfo>> GetSystemsInternalAsync(CancellationToken cancellationToken)
    {
        if (!_isConfigured)
        {
            return [];
        }

        const string cacheKey = "all";
        if (SystemsCache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.CreatedUtc < SystemsCacheDuration)
        {
            return cached.Value.ToList();
        }

        var systems = await GetListAsync<SystemInfo>(
            "SYSTEM?select=sn_number,system_name,system_address,pocet_ssr,popis&order=system_name.asc",
            cancellationToken);

        SystemsCache[cacheKey] = new CacheEntry<IReadOnlyList<SystemInfo>>(DateTimeOffset.UtcNow, systems);
        return systems;
    }

    private async Task<LiveSnapshot?> GetLiveSnapshotInternalAsync(
        string snNumber,
        SystemProfile systemProfile,
        int relayChannelCount,
        double relayCapacityKw,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snNumber))
        {
            return null;
        }

        var cacheKey = snNumber.Trim();
        if (LiveSnapshotCache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.CreatedUtc < LiveSnapshotCacheDuration)
        {
            return cached.Value;
        }

        try
        {
            var encodedSn = Uri.EscapeDataString(snNumber);
            var wattTask = TryGetListAsync<WattRouterInfo>($"WATTROUTER_INFO?sn_number=eq.{encodedSn}&order=created_at.desc&limit=1", cancellationToken);
            var gridTask = TryGetListAsync<GridInformation>($"GRID_INFORMATION?sn_number=eq.{encodedSn}&order=created_at.desc&limit=1", cancellationToken);
            var batteryTask = TryGetListAsync<BatteryInformation>($"BATTERY_INFORMATION?sn_number=eq.{encodedSn}&order=created_at.desc&limit=1", cancellationToken);
            var pvTask = TryGetListAsync<PvInformation>($"PV_INFORMATION?sn_number=eq.{encodedSn}&order=created_at.desc&limit=16", cancellationToken);

            await Task.WhenAll(wattTask, gridTask, batteryTask, pvTask);

            var latestWatt = wattTask.Result.FirstOrDefault();
            var latestGrid = gridTask.Result.FirstOrDefault();
            var latestBattery = batteryTask.Result.FirstOrDefault();
            var pvList = pvTask.Result;

            if (latestWatt is null && latestGrid is null && latestBattery is null && pvList.Count == 0)
            {
                return null;
            }

            var latestPvGroup = GetLatestPvChannelSnapshot(pvList);

            var orderedPv = latestPvGroup.OrderBy(x => x.mppt).ToArray();
            var mppt1 = orderedPv.FirstOrDefault(x => x.mppt == 1);
            var mppt2 = orderedPv.FirstOrDefault(x => x.mppt == 2);

            if (mppt1 is null && mppt2 is null)
            {
                mppt1 = orderedPv.ElementAtOrDefault(0);
                mppt2 = orderedPv.ElementAtOrDefault(1);
            }
            var pvTotal = latestPvGroup.Sum(x => x.power ?? 0);
            var pvVoltage = latestPvGroup.Length > 0
                ? latestPvGroup.Where(x => x.voltage.HasValue).Select(x => (double)x.voltage!.Value).DefaultIfEmpty().Average()
                : 0;
            var pvCurrent = latestPvGroup.Length > 0
                ? latestPvGroup.Where(x => x.current.HasValue).Select(x => (double)x.current!.Value).DefaultIfEmpty().Average()
                : 0;
            var gridPower = latestGrid?.active_power_pcc_total ?? 0;
            var inverterPower = latestGrid?.active_power_output_total ?? 0;
            var batteryPower = latestBattery?.power ?? 0;
            var consumption = ComputeConsumptionKw(gridPower, inverterPower, pvTotal, batteryPower);
            var wattPowerKw = latestWatt != null ? ComputeWattPowerKw(latestWatt, relayCapacityKw) : 0;

            var snapshot = new LiveSnapshot
            {
                time = latestWatt?.created_at ?? latestGrid?.created_at ?? latestBattery?.created_at ?? DateTimeOffset.UtcNow,
                wattPowerPercentage = latestWatt?.powerPercentage,
                wattPowerKw = Math.Round(wattPowerKw, 2),
                relayCount = latestWatt?.ActiveChannelCount ?? 0,
                relayAverageLoadPct = Math.Round(latestWatt?.GetAverageChannelLoadPercentage(relayChannelCount) ?? 0, 1),
                wattUtilizationPct = ComputeWattUtilizationPct(latestWatt, relayChannelCount),
                gridPower = Math.Round(gridPower, 2),
                inverterPower = Math.Round(inverterPower, 2),
                gridFrequency = Math.Round(latestGrid?.grid_frequency ?? 0, 2),
                pvPower = Math.Round(pvTotal, 2),
                pvTotal = Math.Round(pvTotal, 2),
                pvSaturationPct = systemProfile.InstalledPvKw > 0 ? Math.Round(pvTotal / systemProfile.InstalledPvKw * 100, 1) : 0,
                pvVoltage = Math.Round(pvVoltage, 1),
                pvCurrent = Math.Round(pvCurrent, 1),
                mppt1Power = Math.Round(mppt1?.power ?? 0, 2),
                mppt2Power = Math.Round(mppt2?.power ?? 0, 2),
                mppt1Voltage = Math.Round(mppt1?.voltage ?? 0, 1),
                mppt2Voltage = Math.Round(mppt2?.voltage ?? 0, 1),
                mppt1Current = Math.Round(mppt1?.current ?? 0, 1),
                mppt2Current = Math.Round(mppt2?.current ?? 0, 1),
                batteryPower = Math.Round(batteryPower, 2),
                batteryVoltage = Math.Round(latestBattery?.voltage ?? 0, 1),
                batteryCurrent = Math.Round(latestBattery?.current ?? 0, 1),
                batteryTemperature = latestBattery?.temperature,
                consumption = Math.Round(consumption, 2),
                soc = latestBattery?.soc,
                soh = latestBattery?.soh,
                chargeCycle = latestBattery?.charge_cycle,
                gridFetch = latestWatt?.gridFetch,
                relayStates = latestWatt?.ToRelayStates(relayCapacityKw, relayChannelCount) ?? []
            };

            LiveSnapshotCache[cacheKey] = new CacheEntry<LiveSnapshot?>(DateTimeOffset.UtcNow, snapshot);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch live snapshot for {SnNumber}", snNumber);
            return null;
        }
    }

    private async Task<List<T>> GetListAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        if (!_isConfigured)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        using var response = await _httpClient.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var data = await JsonSerializer.DeserializeAsync<List<T>>(
            stream,
            JsonOptions,
            timeout.Token);

        return NormalizeDatabaseTimestamps(data ?? []);
    }

    private async Task<List<T>> GetPagedListAsync<T>(
        string relativeUrl,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var allRows = new List<T>();
        var offset = 0;
        var pageSize = Math.Min(SupabasePageSize, Math.Max(1, maxRows));

        while (allRows.Count < maxRows)
        {
            var separator = relativeUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var pageUrl = $"{relativeUrl}{separator}limit={pageSize}&offset={offset}";
            var page = await GetListAsync<T>(pageUrl, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            allRows.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        return allRows.Count > maxRows
            ? allRows.Take(maxRows).ToList()
            : allRows;
    }

    private async Task<List<StatisticalInformation>> GetHistoryStatsRowsAsync(
        string snNumber,
        DateTime? referenceDay,
        int? days,
        CancellationToken cancellationToken)
    {
        var encodedSn = Uri.EscapeDataString(snNumber);
        const string columns = "sn_number,timestamp,pv_generation_today,pv_generation_total,consumption_today,consumption_total,purchase_today,purchase_total,sell_today,sell_total,battery_charge_today,battery_charge_total,battery_discharge_today,battery_discharge_total";
        var baseUrl = $"STATISTICAL_INFORMATION?select={columns}&sn_number=eq.{encodedSn}";

        if (!days.HasValue)
        {
            return await GetPagedListAsync<StatisticalInformation>(
                $"{baseUrl}&order=timestamp.desc",
                StatsMaxRows,
                cancellationToken);
        }

        var safeDays = Math.Clamp(days.Value, 1, StatsHistoryDays);
        var referenceLocalDate = referenceDay?.Date ?? ToLocalAppTime(DateTimeOffset.UtcNow).DateTime.Date;
        var localStart = DateTime.SpecifyKind(referenceLocalDate.AddDays(-(safeDays - 1)), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(referenceLocalDate.AddDays(1).Add(DailyStatsPreviousDayCutoff), DateTimeKind.Unspecified);
        var fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, _timeZone));
        var toUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeZone));
        var fromStr = Uri.EscapeDataString(ToDatabaseTimestamp(fromUtc).ToString("O"));
        var toStr = Uri.EscapeDataString(ToDatabaseTimestamp(toUtc).ToString("O"));
        var maxRows = Math.Min(StatsMaxRows, Math.Max(SupabasePageSize, safeDays * EstimatedHistoryRowsPerDay));

        return await GetPagedListAsync<StatisticalInformation>(
            $"{baseUrl}&timestamp=gte.{fromStr}&timestamp=lt.{toStr}&order=timestamp.desc",
            maxRows,
            cancellationToken);
    }

    private async Task<List<T>> TryGetListAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await GetListAsync<T>(relativeUrl, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Supabase query timed out for {Url}", relativeUrl);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Partial live query failed for {Url}", relativeUrl);
            return [];
        }
    }

    private async Task<List<T>> RunDashboardQueryAsync<T>(
        string label,
        Func<CancellationToken, Task<List<T>>> queryFactory,
        CancellationToken cancellationToken)
    {
        using var queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queryTask = queryFactory(queryTimeout.Token);
        var timeoutTask = Task.Delay(QueryTimeout + TimeSpan.FromSeconds(2), cancellationToken);
        var completedTask = await Task.WhenAny(queryTask, timeoutTask);

        if (completedTask == queryTask)
        {
            return await queryTask;
        }

        queryTimeout.Cancel();
        _logger.LogWarning("Dashboard query {Label} did not finish inside {TimeoutSeconds} seconds. Continuing with empty data for this source.", label, QueryTimeout.TotalSeconds + 2);
        return [];
    }

    private async Task<List<T>> TryGetPagedListAsync<T>(
        string relativeUrl,
        int maxRows,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetPagedListAsync<T>(relativeUrl, maxRows, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Supabase paged query timed out for {Url}", relativeUrl);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Partial paged query failed for {Url}", relativeUrl);
            return [];
        }
    }

    private static DateTimeOffset ToDatabaseTimestamp(DateTimeOffset appTimestamp)
    {
        return appTimestamp.Add(DatabaseTimestampOffset);
    }

    private static DateTimeOffset NormalizeDatabaseTimestamp(DateTimeOffset databaseTimestamp)
    {
        return databaseTimestamp.Add(-DatabaseTimestampOffset);
    }

    private DateTimeOffset ToLocalAppTime(DateTimeOffset timestamp)
    {
        return TimeZoneInfo.ConvertTime(timestamp, _timeZone);
    }

    private DateTimeOffset ToDashboardDisplayTime(DateTimeOffset timestamp)
    {
        // DB timestamps are consistently one hour ahead of the real local timeline.
        // We therefore subtract one hour again at the dashboard display/grouping layer.
        return TimeZoneInfo.ConvertTime(timestamp.Add(-DatabaseTimestampOffset), _timeZone);
    }

    private static List<T> NormalizeDatabaseTimestamps<T>(List<T> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        if (rows is List<WattRouterInfo> wattRows)
        {
            wattRows.ForEach(row => row.created_at = NormalizeDatabaseTimestamp(row.created_at));
            return rows;
        }

        if (rows is List<PvInformation> pvRows)
        {
            pvRows.ForEach(row => row.created_at = NormalizeDatabaseTimestamp(row.created_at));
            return rows;
        }

        if (rows is List<BatteryInformation> batteryRows)
        {
            batteryRows.ForEach(row => row.created_at = NormalizeDatabaseTimestamp(row.created_at));
            return rows;
        }

        if (rows is List<GridInformation> gridRows)
        {
            gridRows.ForEach(row => row.created_at = NormalizeDatabaseTimestamp(row.created_at));
            return rows;
        }

        if (rows is List<StatisticalInformation> statRows)
        {
            statRows.ForEach(row => row.timestamp = NormalizeDatabaseTimestamp(row.timestamp));
            return rows;
        }

        if (rows is List<SystemFreshnessRow> freshnessRows)
        {
            return freshnessRows
                .Select(row => (T)(object)(row with { created_at = NormalizeDatabaseTimestamp(row.created_at) }))
                .ToList();
        }

        if (rows is List<SystemStatsFreshnessRow> statsFreshnessRows)
        {
            return statsFreshnessRows
                .Select(row => (T)(object)(row with { timestamp = NormalizeDatabaseTimestamp(row.timestamp) }))
                .ToList();
        }

        return rows;
    }

    private async Task<string> ResolveDefaultSystemSnAsync(IReadOnlyList<SystemInfo> systems, CancellationToken cancellationToken)
    {
        if (systems.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            var systemSnSet = systems
                .Select(x => x.sn_number)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var latestStatRows = await GetListAsync<SystemStatsFreshnessRow>(
                "STATISTICAL_INFORMATION?select=sn_number,timestamp&order=timestamp.desc&limit=150",
                cancellationToken);

            foreach (var row in latestStatRows)
            {
                if (!string.IsNullOrWhiteSpace(row.sn_number) && systemSnSet.Contains(row.sn_number))
                {
                    return row.sn_number;
                }
            }

            var latestRows = await GetListAsync<SystemFreshnessRow>(
                "WATTROUTER_INFO?select=sn_number,created_at&order=created_at.desc&limit=50",
                cancellationToken);

            foreach (var row in latestRows)
            {
                if (!string.IsNullOrWhiteSpace(row.sn_number) && systemSnSet.Contains(row.sn_number))
                {
                    return row.sn_number;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve default system from recent telemetry");
        }

        return systems[0].sn_number;
    }

    private async Task<DateTimeOffset?> ResolveTelemetryReferenceTimeAsync(string snNumber, CancellationToken cancellationToken)
    {
        var encodedSn = Uri.EscapeDataString(snNumber);

        var wattTask = TryGetListAsync<SystemFreshnessRow>(
            $"WATTROUTER_INFO?select=sn_number,created_at&sn_number=eq.{encodedSn}&order=created_at.desc&limit=1",
            cancellationToken);
        var gridTask = TryGetListAsync<SystemFreshnessRow>(
            $"GRID_INFORMATION?select=sn_number,created_at&sn_number=eq.{encodedSn}&order=created_at.desc&limit=1",
            cancellationToken);
        var batteryTask = TryGetListAsync<SystemFreshnessRow>(
            $"BATTERY_INFORMATION?select=sn_number,created_at&sn_number=eq.{encodedSn}&order=created_at.desc&limit=1",
            cancellationToken);
        var pvTask = TryGetListAsync<SystemFreshnessRow>(
            $"PV_INFORMATION?select=sn_number,created_at&sn_number=eq.{encodedSn}&order=created_at.desc&limit=1",
            cancellationToken);

        await Task.WhenAll(wattTask, gridTask, batteryTask, pvTask);

        var timestamps = new[]
        {
            wattTask.Result.FirstOrDefault()?.created_at,
            gridTask.Result.FirstOrDefault()?.created_at,
            batteryTask.Result.FirstOrDefault()?.created_at,
            pvTask.Result.FirstOrDefault()?.created_at
        }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();

        if (timestamps.Length == 0)
        {
            return null;
        }

        return timestamps.Max();
    }

    private (DateTimeOffset FromUtc, DateTimeOffset ToUtc) BuildWindow(DateTime? day, int hoursBack, DateTimeOffset? referenceEndUtc = null)
    {
        if (day.HasValue)
        {
            var localDayEnd = DateTime.SpecifyKind(day.Value.Date, DateTimeKind.Unspecified).AddDays(1);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(localDayEnd, _timeZone);
            var startUtc = endUtc.AddHours(-Math.Max(hoursBack, 1));

            return (new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
        }

        var toUtc = referenceEndUtc ?? DateTimeOffset.UtcNow;
        var fromUtc = toUtc.AddHours(-hoursBack);
        return (fromUtc, toUtc);
    }

    private (DateTimeOffset FromUtc, DateTimeOffset ToUtc) BuildStatsDayWindow(DateTime day)
    {
        var localStart = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1).Add(DailyStatsPreviousDayCutoff);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, _timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeZone);

        return (new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
    }

    private static int NormalizeHoursBack(int hoursBack)
    {
        return hoursBack switch
        {
            <= 0 => 24,
            > 168 => 168,
            _ => hoursBack
        };
    }

    private static TimeZoneInfo ResolveDashboardTimeZone()
    {
        var candidateIds = new[]
        {
            "Europe/Bratislava",
            "Central Europe Standard Time",
            "W. Europe Standard Time"
        };

        foreach (var candidateId in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidateId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private SystemProfile ResolveSystemProfile(string? snNumber)
    {
        if (!string.IsNullOrWhiteSpace(snNumber) &&
            _catalog.SystemProfiles.TryGetValue(snNumber, out var profile))
        {
            return profile;
        }

        return _catalog.DefaultSystemProfile ?? new SystemProfile();
    }

    private static int ResolveRelayChannelCount(int? relayChannelCount)
    {
        if (!relayChannelCount.HasValue || relayChannelCount.Value <= 0)
        {
            return DefaultRelayChannelCount;
        }

        return Math.Clamp(relayChannelCount.Value, 1, MaxRelayChannelCount);
    }

    private static double ResolveRelayCapacityKw(SystemProfile systemProfile, SystemInfo? system, int relayChannelCount)
    {
        var normalizedRelayCount = ResolveRelayChannelCount(relayChannelCount);
        var parsedRelayCapacityKw = TryParseRelayCapacityKw(system?.popis, normalizedRelayCount);
        if (parsedRelayCapacityKw.HasValue && parsedRelayCapacityKw.Value > 0)
        {
            return parsedRelayCapacityKw.Value;
        }

        if (systemProfile.WattMaxKw > 0)
        {
            return Math.Round(systemProfile.WattMaxKw / normalizedRelayCount, 3);
        }

        return DefaultRelayCapacityKw;
    }

    private static double? TryParseRelayCapacityKw(string? description, int relayChannelCount)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalizedDescription = description.Trim();
        var relayMatch = RelayCountAndPowerRegex.Match(normalizedDescription);
        if (relayMatch.Success)
        {
            var parsedCount = ParseInvariantDouble(relayMatch.Groups["count"].Value);
            var parsedPower = ParseInvariantDouble(relayMatch.Groups["power"].Value);
            if (parsedPower > 0)
            {
                if (parsedCount <= 0 || Math.Abs(parsedCount - relayChannelCount) <= 0.1)
                {
                    return Math.Round(parsedPower, 3);
                }

                return Math.Round(parsedPower, 3);
            }
        }

        var totalMatch = TotalPowerRegex.Match(normalizedDescription);
        if (totalMatch.Success)
        {
            var parsedTotal = ParseInvariantDouble(totalMatch.Groups["total"].Value);
            if (parsedTotal > 0 && relayChannelCount > 0)
            {
                return Math.Round(parsedTotal / relayChannelCount, 3);
            }
        }

        return null;
    }

    private static double ParseInvariantDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var normalized = raw.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static double ComputeWattPowerKw(WattRouterInfo point, double relayCapacityKw)
    {
        var modulated = (point.powerPercentage ?? 0) / 100.0 * relayCapacityKw;
        var relays = point.RelaysOnCount * relayCapacityKw;
        return Math.Round(modulated + relays, 2);
    }

    private static double ComputeWattUtilizationPct(WattRouterInfo? point, int relayChannelCount, double? fallback = null)
    {
        if (point is null)
        {
            return Math.Round(Math.Clamp(fallback ?? 0, 0, 100), 1);
        }

        return Math.Round(Math.Clamp(point.GetAverageChannelLoadPercentage(relayChannelCount), 0, 100), 1);
    }

    private static double ComputeConsumptionKw(double gridKw, double inverterKw, double pvKw, double batteryKw)
    {
        if (Math.Abs(inverterKw) > 0.01)
        {
            return Math.Round(Math.Max(0, Math.Abs(inverterKw - gridKw)), 2);
        }

        return Math.Round(Math.Max(0, pvKw - gridKw - batteryKw), 2);
    }

    private static double SumLatestPvGroup(IReadOnlyList<PvInformation> pvPoints)
    {
        return Math.Round(GetLatestPvChannelSnapshot(pvPoints).Sum(x => x.power ?? 0), 2);
    }

    private static PvInformation[] GetLatestPvChannelSnapshot(IReadOnlyList<PvInformation> pvPoints)
    {
        if (pvPoints.Count == 0)
        {
            return [];
        }

        var latest = pvPoints.Max(x => x.created_at);
        var freshRows = pvPoints
            .Where(x => latest - x.created_at <= PvChannelMergeWindow)
            .ToArray();

        var latestPerMppt = freshRows
            .Where(x => x.mppt.HasValue)
            .GroupBy(x => x.mppt!.Value)
            .Select(group => group.OrderByDescending(x => x.created_at).First())
            .OrderBy(x => x.mppt)
            .ToArray();

        if (latestPerMppt.Length > 0)
        {
            return latestPerMppt;
        }

        return pvPoints
            .Where(x => x.created_at == latest)
            .OrderBy(x => x.mppt)
            .ToArray();
    }

    private IReadOnlyList<DailyStatsDay> BuildDailyStatsSegments(IReadOnlyList<StatisticalInformation> rawStats)
    {
        if (rawStats.Count == 0)
        {
            return [];
        }

        var ordered = rawStats
            .OrderBy(x => x.timestamp)
            .Select(x => new DailyStatsSample(x, ToLocalAppTime(x.timestamp).DateTime, ComputeDailyCounterTotal(x)))
            .ToArray();

        var dailySegments = new Dictionary<DateTime, DailyStatsDay>();
        var currentSegment = new List<DailyStatsSample>();
        var currentSegmentStartedAfterReset = false;

        foreach (var sample in ordered)
        {
            if (currentSegment.Count > 0 && IsDailyCounterReset(currentSegment[^1].Row, sample.Row))
            {
                AddDailyStatsSegment(dailySegments, currentSegment, currentSegmentStartedAfterReset);
                currentSegment = [];
                currentSegmentStartedAfterReset = true;
            }

            currentSegment.Add(sample);
        }

        AddDailyStatsSegment(dailySegments, currentSegment, currentSegmentStartedAfterReset);

        return dailySegments
            .OrderBy(x => x.Key)
            .Select(x => x.Value)
            .ToArray();
    }

    private static List<DailyHistoryPoint> BuildHistory(IReadOnlyList<DailyStatsDay> dailyStats)
    {
        var history = new List<DailyHistoryPoint>(dailyStats.Count);
        foreach (var day in dailyStats)
        {
            var pv = day.Pv;
            var consumption = day.Consumption;
            var import = day.Import;
            var export = day.Export;
            var charge = day.BatteryCharge;
            var discharge = day.BatteryDischarge;
            var directUse = Math.Max(0, consumption - import - discharge);
            var selfUse = Math.Max(0, pv - export);
            var selfSufficiency = Math.Max(0, consumption - import);

            history.Add(new DailyHistoryPoint(
                day.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.LocalDate.ToString("dd.MM.", CultureInfo.InvariantCulture),
                Math.Round(pv, 1),
                Math.Round(consumption, 1),
                Math.Round(import, 1),
                Math.Round(export, 1),
                Math.Round(charge, 1),
                Math.Round(discharge, 1),
                Math.Round(directUse, 1),
                Math.Round(selfUse, 1),
                pv > 0 ? Math.Round(selfUse / pv * 100, 1) : 0,
                Math.Round(selfSufficiency, 1),
                consumption > 0 ? Math.Round(selfSufficiency / consumption * 100, 1) : 0));
        }

        return history;
    }

    private static List<DailyTotalPoint> BuildTotalHistory(IReadOnlyList<DailyStatsDay> dailyStats)
    {
        var history = new List<DailyTotalPoint>(dailyStats.Count);
        foreach (var day in dailyStats)
        {
            var row = day.LatestRow;
            history.Add(new DailyTotalPoint(
                day.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.LocalDate.ToString("dd.MM.", CultureInfo.InvariantCulture),
                Math.Round(row.pv_generation_total ?? 0, 1),
                Math.Round(row.consumption_total ?? 0, 1),
                Math.Round(row.purchase_total ?? 0, 1),
                Math.Round(row.sell_total ?? 0, 1),
                Math.Round(row.battery_charge_total ?? 0, 1),
                Math.Round(row.battery_discharge_total ?? 0, 1)));
        }

        return history;
    }

    private static void AddDailyStatsSegment(
        IDictionary<DateTime, DailyStatsDay> dailySegments,
        IReadOnlyList<DailyStatsSample> segment,
        bool startedAfterReset)
    {
        if (segment.Count == 0)
        {
            return;
        }

        var firstLocal = segment[0].LocalTime;
        var lastLocal = segment[^1].LocalTime;
        var localDate = startedAfterReset || lastLocal.TimeOfDay >= DailyStatsPreviousDayCutoff
            ? firstLocal.Date
            : lastLocal.Date.AddDays(-1);

        var day = new DailyStatsDay(
            localDate,
            segment[^1].Row,
            new DailyStatsAccumulator
        {
            Pv = MaxDailyValue(segment, x => x.pv_generation_today),
            Consumption = MaxDailyValue(segment, x => x.consumption_today),
            Import = MaxDailyValue(segment, x => x.purchase_today),
            Export = MaxDailyValue(segment, x => x.sell_today),
            BatteryCharge = MaxDailyValue(segment, x => x.battery_charge_today),
            BatteryDischarge = MaxDailyValue(segment, x => x.battery_discharge_today)
        });

        if (dailySegments.TryGetValue(localDate, out var existing))
        {
            dailySegments[localDate] = existing.Merge(day);
            return;
        }

        dailySegments[localDate] = day;
    }

    private static double MaxDailyValue(
        IReadOnlyList<DailyStatsSample> segment,
        Func<StatisticalInformation, double?> selector)
    {
        return segment
            .Select(sample => selector(sample.Row) ?? 0)
            .Where(value => value >= 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsDailyCounterReset(StatisticalInformation previous, StatisticalInformation current)
    {
        var previousTotal = ComputeDailyCounterTotal(previous);
        var currentTotal = ComputeDailyCounterTotal(current);
        if (previousTotal >= 5 && currentTotal + 0.5 < previousTotal * 0.35)
        {
            return true;
        }

        var previousPv = previous.pv_generation_today ?? 0;
        var currentPv = current.pv_generation_today ?? 0;
        return previousPv >= 2 && currentPv + 0.2 < previousPv * 0.2;
    }

    private static double ComputeDailyCounterTotal(StatisticalInformation row)
    {
        return Math.Max(0, row.pv_generation_today ?? 0) +
               Math.Max(0, row.consumption_today ?? 0) +
               Math.Max(0, row.purchase_today ?? 0) +
               Math.Max(0, row.sell_today ?? 0) +
               Math.Max(0, row.battery_charge_today ?? 0) +
               Math.Max(0, row.battery_discharge_today ?? 0);
    }

    private StatisticalInformation? ResolveLatestStats(
        IReadOnlyList<DailyStatsDay> dailyStats,
        IReadOnlyList<StatisticalInformation> stats,
        DateTime? day)
    {
        if (stats.Count == 0)
        {
            return null;
        }

        if (!day.HasValue)
        {
            return stats.MaxBy(x => x.timestamp);
        }

        var requestedDay = day.Value.Date;
        return dailyStats.FirstOrDefault(x => x.LocalDate == requestedDay)?.LatestRow;
    }

    private static double ComputeEnergyFromSeries(IReadOnlyList<DateTimeOffset> timestamps, IReadOnlyList<double> powerKwSeries)
    {
        if (timestamps.Count < 2 || powerKwSeries.Count != timestamps.Count)
        {
            return 0;
        }

        var totalKwh = 0d;
        for (var i = 1; i < timestamps.Count; i++)
        {
            var durationHours = (timestamps[i] - timestamps[i - 1]).TotalHours;
            if (durationHours <= 0)
            {
                continue;
            }

            var avgPower = (powerKwSeries[i] + powerKwSeries[i - 1]) / 2d;
            totalKwh += avgPower * durationHours;
        }

        return Math.Round(totalKwh, 2);
    }

    private static double ComputeEnergyScore(
        double selfConsumptionPct,
        double selfSufficiencyPct,
        double exportLossPct,
        double freshnessMinutes,
        int batterySoc,
        double pvSaturationPct,
        double wattUtilizationPct)
    {
        var score =
            (selfConsumptionPct * 0.24) +
            (selfSufficiencyPct * 0.24) +
            ((100 - exportLossPct) * 0.12) +
            (Math.Clamp(batterySoc, 0, 100) * 0.14) +
            (Math.Max(0, 100 - freshnessMinutes * 3) * 0.1) +
            (Math.Clamp(pvSaturationPct, 0, 100) * 0.08) +
            (Math.Min(100, wattUtilizationPct) * 0.08);

        return Math.Round(Math.Clamp(score, 0, 100), 0);
    }

    private static double ComputeBaseLoadKw(IReadOnlyList<TimeSeriesPoint> timeline)
    {
        var values = timeline
            .Select(x => x.ConsumptionKw)
            .Where(x => x > 0.05)
            .OrderBy(x => x)
            .ToArray();

        if (values.Length == 0)
        {
            return 0;
        }

        var take = Math.Max(1, values.Length / 8);
        return Math.Round(values.Take(take).Average(), 2);
    }

    private static double ComputeMpptImbalancePct(double mppt1Kw, double mppt2Kw)
    {
        var strongest = Math.Max(Math.Abs(mppt1Kw), Math.Abs(mppt2Kw));
        if (strongest <= 0.05)
        {
            return 0;
        }

        return Math.Clamp(Math.Abs(mppt1Kw - mppt2Kw) / strongest * 100d, 0, 100);
    }

    private static double ProjectMonthly(IEnumerable<double> source, double fallbackTodayValue)
    {
        var recent = source.TakeLast(30).ToArray();
        if (recent.Length == 0)
        {
            return Math.Round(fallbackTodayValue * 30, 1);
        }

        return Math.Round(recent.Average() * 30, 1);
    }

    private IReadOnlyList<TariffBenchmarkResult> BuildTariffBenchmarks(
        double dailySelfSufficiencyKwh,
        double monthlySelfSufficiencyKwh,
        double annualSelfSufficiencyKwh,
        double lifetimeSelfSufficiencyKwh)
    {
        return _catalog.TariffBenchmarks
            .Select(item =>
            {
                var providerKey = string.IsNullOrWhiteSpace(item.ProviderKey) ? item.Key : item.ProviderKey;
                var lowTariffShare = Math.Clamp(item.LowTariffSharePct, 0, 100) / 100d;
                var effectiveRate = item.LowRateEurPerKwh.HasValue
                    ? (item.HighRateEurPerKwh * (1 - lowTariffShare)) + (item.LowRateEurPerKwh.Value * lowTariffShare)
                    : item.HighRateEurPerKwh;
                var dailyAvoided = Math.Round(dailySelfSufficiencyKwh * effectiveRate, 2);
                var monthlyAvoided = Math.Round(monthlySelfSufficiencyKwh * effectiveRate, 2);
                var annualAvoided = Math.Round(annualSelfSufficiencyKwh * effectiveRate, 2);
                var lifetimeAvoided = Math.Round(lifetimeSelfSufficiencyKwh * effectiveRate, 2);
                var annualFixed = Math.Round(Math.Max(0, item.MonthlyFixedFeeEur) * 12d, 2);
                var dailyFixed = Math.Round(Math.Max(0, item.MonthlyFixedFeeEur) / 30d, 2);

                return new TariffBenchmarkResult
                {
                    Key = item.Key,
                    ProviderKey = providerKey,
                    ProviderName = item.ProviderName,
                    DistributorName = item.DistributorName,
                    TariffCode = item.TariffCode,
                    TariffLabel = item.TariffLabel,
                    ProductType = item.ProductType,
                    EffectiveDate = item.EffectiveDate,
                    Notes = item.Notes,
                    AssumptionLabel = item.AssumptionLabel,
                    SourceLabel = item.SourceLabel,
                    SourceUrl = item.SourceUrl,
                    HighRateEurPerKwh = item.HighRateEurPerKwh,
                    LowRateEurPerKwh = item.LowRateEurPerKwh,
                    EffectiveImportRateEurPerKwh = Math.Round(effectiveRate, 6),
                    MonthlyFixedFeeEur = Math.Round(Math.Max(0, item.MonthlyFixedFeeEur), 2),
                    AnnualFixedFeeEur = annualFixed,
                    EstimatedLowTariffSharePct = Math.Round(item.LowTariffSharePct, 1),
                    IncludesDistribution = item.IncludesDistribution,
                    IncludesVat = item.IncludesVat,
                    DailyAvoidedCostEur = dailyAvoided,
                    MonthlyAvoidedCostEur = monthlyAvoided,
                    AnnualAvoidedCostEur = annualAvoided,
                    LifetimeAvoidedCostEur = lifetimeAvoided,
                    NetDailyBenefitEur = Math.Round(dailyAvoided - dailyFixed, 2),
                    NetMonthlyBenefitEur = Math.Round(monthlyAvoided - item.MonthlyFixedFeeEur, 2),
                    NetAnnualBenefitEur = Math.Round(annualAvoided - annualFixed, 2)
                };
            })
            .OrderBy(item => item.ProviderName)
            .ThenBy(item => item.TariffCode)
            .ToArray();
    }

    private ExportRevenueSummary BuildExportRevenue(
        double todayExportKwh,
        double monthlyExportKwh,
        double annualExportKwh,
        double lifetimeExportKwh)
    {
        var benchmark = _catalog.ExportBenchmark ?? new ExportBenchmark();

        return new ExportRevenueSummary
        {
            Label = benchmark.Label,
            EffectiveDate = benchmark.EffectiveDate,
            Notes = benchmark.Notes,
            SourceLabel = benchmark.SourceLabel,
            SourceUrl = benchmark.SourceUrl,
            RateEurPerKwh = benchmark.RateEurPerKwh,
            DailyRevenueEur = Math.Round(todayExportKwh * benchmark.RateEurPerKwh, 2),
            MonthlyRevenueEur = Math.Round(monthlyExportKwh * benchmark.RateEurPerKwh, 2),
            AnnualRevenueEur = Math.Round(annualExportKwh * benchmark.RateEurPerKwh, 2),
            LifetimeRevenueEur = Math.Round(lifetimeExportKwh * benchmark.RateEurPerKwh, 2)
        };
    }

    private EnvironmentalBenefitSummary BuildEnvironmentalBenefits(
        double todayPvKwh,
        double monthlyPvKwh,
        double annualPvKwh,
        double lifetimePvKwh,
        IReadOnlyList<TariffBenchmarkResult> tariffBenchmarks,
        ExportRevenueSummary exportRevenue)
    {
        var factors = _catalog.EnvironmentalFactors ?? new EnvironmentalFactors();
        var bestDailyNet = tariffBenchmarks.Count > 0 ? tariffBenchmarks.Max(x => x.NetDailyBenefitEur) : 0;
        var bestMonthlyNet = tariffBenchmarks.Count > 0 ? tariffBenchmarks.Max(x => x.NetMonthlyBenefitEur) : 0;
        var bestAnnualNet = tariffBenchmarks.Count > 0 ? tariffBenchmarks.Max(x => x.NetAnnualBenefitEur) : 0;
        var bestLifetimeAvoided = tariffBenchmarks.Count > 0 ? tariffBenchmarks.Max(x => x.LifetimeAvoidedCostEur) : 0;
        var co2TodayKg = todayPvKwh * factors.Co2KgPerKwh;
        var co2MonthlyKg = monthlyPvKwh * factors.Co2KgPerKwh;
        var co2AnnualKg = annualPvKwh * factors.Co2KgPerKwh;
        var co2LifetimeKg = lifetimePvKwh * factors.Co2KgPerKwh;
        var coalTodayKg = todayPvKwh * factors.CoalKgPerKwh;
        var coalAnnualKg = annualPvKwh * factors.CoalKgPerKwh;
        var coalLifetimeKg = lifetimePvKwh * factors.CoalKgPerKwh;

        return new EnvironmentalBenefitSummary
        {
            TodayCo2SavedKg = Math.Round(co2TodayKg, 1),
            MonthlyCo2SavedKg = Math.Round(co2MonthlyKg, 1),
            AnnualCo2SavedTons = Math.Round(co2AnnualKg / 1000d, 2),
            Co2SavedTons = Math.Round(co2LifetimeKg / 1000d, 2),
            TodayCoalSavedKg = Math.Round(coalTodayKg, 1),
            AnnualCoalSavedTons = Math.Round(coalAnnualKg / 1000d, 2),
            CoalSavedTons = Math.Round(coalLifetimeKg / 1000d, 2),
            TreesEquivalent = factors.TreeKgCo2PerYear > 0 ? Math.Round(co2LifetimeKg / factors.TreeKgCo2PerYear, 0) : 0,
            AnnualTreesEquivalent = factors.TreeKgCo2PerYear > 0 ? Math.Round(co2AnnualKg / factors.TreeKgCo2PerYear, 0) : 0,
            TodayYieldEur = Math.Round(bestDailyNet + exportRevenue.DailyRevenueEur, 2),
            MonthlyYieldEur = Math.Round(bestMonthlyNet + exportRevenue.MonthlyRevenueEur, 2),
            AnnualYieldEur = Math.Round(bestAnnualNet + exportRevenue.AnnualRevenueEur, 2),
            LifetimeYieldEur = Math.Round(bestLifetimeAvoided + exportRevenue.LifetimeRevenueEur, 2)
        };
    }

    private IReadOnlyList<StatusPill> BuildStatusPills(
        LiveSnapshot? live,
        BatteryInformation? battery,
        double freshnessMinutes,
        double selfSufficiencyPct,
        double gridDependencyPct,
        double pvSaturationPct,
        double wattUtilizationPct)
    {
        var gridPowerOverride = live?.gridPower ?? 0;
        var gridStateOverride = gridPowerOverride switch
        {
            > 0.15 => new StatusPill("Siet", "Export", "good"),
            < -0.15 => new StatusPill("Siet", "Import", "warning"),
            _ => new StatusPill("Siet", "Rovnovaha", "neutral")
        };

        var batteryStateOverride = (battery?.power ?? 0) switch
        {
            > 0.15f => new StatusPill("Bateria", "Nabijanie", "good"),
            < -0.15f => new StatusPill("Bateria", "Vybijanie", "accent"),
            _ => new StatusPill("Bateria", "Stabilna", "neutral")
        };

        var freshnessStateOverride = freshnessMinutes switch
        {
            <= 5 => new StatusPill("Telemetria", "Live", "good"),
            <= 20 => new StatusPill("Telemetria", "Mierne oneskorenie", "warning"),
            _ => new StatusPill("Telemetria", "Skontroluj sync", "danger")
        };

        var autonomyStateOverride = selfSufficiencyPct switch
        {
            >= 80 => new StatusPill("Sebestacnost", "Silna", "good"),
            >= 55 => new StatusPill("Sebestacnost", "Vyrovnana", "accent"),
            _ => new StatusPill("Sebestacnost", $"Siet {gridDependencyPct:0.#} %", "warning")
        };

        var pvStateOverride = pvSaturationPct switch
        {
            >= 90 => new StatusPill("PV limit", $"{pvSaturationPct:0.#} %", "good"),
            >= 55 => new StatusPill("PV limit", $"{pvSaturationPct:0.#} %", "accent"),
            _ => new StatusPill("PV limit", $"{pvSaturationPct:0.#} %", "neutral")
        };

        var wattStateOverride = wattUtilizationPct switch
        {
            >= 60 => new StatusPill("WattRouter", $"{wattUtilizationPct:0.#} %", "good"),
            > 5 => new StatusPill("WattRouter", $"{wattUtilizationPct:0.#} %", "accent"),
            _ => new StatusPill("WattRouter", "Idle", "neutral")
        };

        return [gridStateOverride, batteryStateOverride, freshnessStateOverride, autonomyStateOverride, pvStateOverride, wattStateOverride];
    }

    private IReadOnlyList<DeviceState> BuildDeviceStates(
        double pvKw,
        double inverterKw,
        double consumptionKw,
        double gridKw,
        double batteryKw,
        double wattKw,
        BatteryInformation? battery,
        double pvSaturationPct,
        double wattUtilizationPct,
        double relayAverageLoadPct,
        WattRouterInfo? watt)
    {
        return
        [
            new DeviceState("PV pole", $"{pvKw:0.00} kW", $"Saturacia {pvSaturationPct:0.#} % z instalovaneho vykonu", pvKw > 0.1 ? "good" : "neutral"),
            new DeviceState("Menic", $"{inverterKw:0.00} kW", "Hlavny AC uzol systemu", Math.Abs(inverterKw) > 0.1 ? "accent" : "neutral"),
            new DeviceState("Domácnosť", $"{consumptionKw:0.00} kW", "Aktuálny odber objektu", consumptionKw > 0.1 ? "accent" : "neutral"),
            new DeviceState("Sieť", $"{gridKw:0.00} kW", gridKw > 0 ? "Prebytky smerujú do siete" : gridKw < 0 ? "Dom odoberá zo siete" : "Tok je takmer nulový", gridKw > 0 ? "good" : gridKw < 0 ? "warning" : "neutral"),
            new DeviceState("Batéria", $"{batteryKw:0.00} kW", $"SOC {(battery?.soc ?? 0):0} %, SOH {(battery?.soh ?? 0):0} %", batteryKw > 0 ? "good" : batteryKw < 0 ? "accent" : "neutral"),
            new DeviceState("WattRouter", $"{wattKw:0.00} kW", $"Vyťaženie {wattUtilizationPct:0.#} %, priemer SSR {relayAverageLoadPct:0.#} %", wattKw > 0.1 ? "good" : "neutral")
        ];
    }

    private IReadOnlyList<InsightItem> BuildInsights(
        double todayPv,
        double todayConsumption,
        double todayImport,
        double todayExport,
        double selfConsumptionPct,
        double selfSufficiencyPct,
        int batterySoc,
        double latestGridKw,
        double latestWattKw,
        double freshnessMinutes)
    {
        var insightsOverride = new List<InsightItem>();

        if (todayExport > 1.5 && selfConsumptionPct < 75)
        {
            insightsOverride.Add(new InsightItem(
                "Presuňte viac spotreby do slnečného okna",
                $"Do siete dnes odišlo {todayExport:0.0} kWh. Ohrev vody, nabíjanie alebo flexibilné spotrebiče sa oplatí presunúť do času prebytkov.",
                "warning"));
        }

        if (todayImport > 2 && selfSufficiencyPct < 60)
        {
            insightsOverride.Add(new InsightItem(
                "Nočný import je stále citeľný",
                $"Import zo siete dosiahol {todayImport:0.0} kWh pri spotrebe {todayConsumption:0.0} kWh. Oplatí sa sledovať trvalé odbery a ponechať rezervu batérie na večer.",
                "accent"));
        }

        if (batterySoc < 20 && latestGridKw < -0.2)
        {
            insightsOverride.Add(new InsightItem(
                "Batéria je nízko",
                $"SOC batérie je len {batterySoc:0} %. Pri ďalšej importnej špičke zvážte ochranný limit alebo presun záťaže mimo večernej špičky.",
                "danger"));
        }

        if (latestWattKw > 0.3)
        {
            insightsOverride.Add(new InsightItem(
                "WattRouter odkláňa prebytky",
                $"Do ohrevu smeruje približne {latestWattKw:0.00} kW. To pomáha tlmiť export a zvyšuje vlastnú spotrebu.",
                "good"));
        }

        if (todayPv > 0 && selfConsumptionPct >= 85 && selfSufficiencyPct >= 70)
        {
            insightsOverride.Add(new InsightItem(
                "Dnešný režim je veľmi efektívny",
                $"Vlastná spotreba {selfConsumptionPct:0.#} % a sebestačnosť {selfSufficiencyPct:0.#} % sú nad štandardom.",
                "good"));
        }

        if (freshnessMinutes > 20)
        {
            insightsOverride.Add(new InsightItem(
                "Živá telemetria mešká",
                $"Posledný snapshot je starý približne {freshnessMinutes:0.#} min. Skontrolujte logger, komunikáciu meniča alebo ingest do Supabase.",
                "danger"));
        }

        if (insightsOverride.Count == 0)
        {
            insightsOverride.Add(new InsightItem(
                "Systém pracuje stabilne",
                "Aktuálne nevidíme výrazný problém. Sledujte trend importu, využitie WattRoutera a dlhodobé zaťaženie batérie.",
                "neutral"));
        }

        return insightsOverride;
    }

    private static SmartForecastSummary BuildSmartForecast(
        IReadOnlyList<DailyHistoryPoint> history,
        double todayPv,
        double todayConsumption,
        double todayImport,
        double todayExport)
    {
        var recent = history.TakeLast(14).ToArray();

        double Forecast(Func<DailyHistoryPoint, double> selector, double fallback)
        {
            if (recent.Length == 0)
            {
                return Math.Round(Math.Max(0, fallback), 1);
            }

            var last7 = recent.TakeLast(7).Select(selector).ToArray();
            var last3 = recent.TakeLast(3).Select(selector).ToArray();
            var baseline = last7.Length > 0 ? last7.Average() : fallback;
            var momentum = last3.Length > 0 ? last3.Average() : baseline;
            var trend = recent.Length >= 6
                ? recent.TakeLast(3).Select(selector).Average() - recent.Take(3).Select(selector).Average()
                : 0;

            return Math.Round(Math.Max(0, (baseline * 0.58) + (momentum * 0.34) + (trend * 0.08)), 1);
        }

        var pv = Forecast(x => x.Pv, todayPv);
        var consumption = Forecast(x => x.Consumption, todayConsumption);
        var import = Forecast(x => x.Import, todayImport);
        var export = Forecast(x => x.Export, todayExport);

        var pvValues = recent.Select(x => x.Pv).Where(x => x > 0).ToArray();
        var variability = 0d;
        if (pvValues.Length > 1)
        {
            var average = pvValues.Average();
            var variance = pvValues.Sum(x => Math.Pow(x - average, 2)) / pvValues.Length;
            variability = average > 0 ? Math.Sqrt(variance) / average : 1;
        }

        var confidence = Math.Round(Math.Clamp((recent.Length / 14d * 68) + ((1 - Math.Min(1, variability)) * 32), 25, 94), 0);
        var summary = export > import
            ? $"Očakávaný prebytok približne {Math.Round(export - import, 1):0.0} kWh."
            : $"Očakávaný import približne {Math.Round(import - export, 1):0.0} kWh.";

        return new SmartForecastSummary
        {
            TomorrowPvKwh = pv,
            TomorrowConsumptionKwh = consumption,
            TomorrowImportKwh = import,
            TomorrowExportKwh = export,
            ConfidencePct = confidence,
            Summary = summary,
            Metrics =
            [
                new ForecastMetric("FV zajtra", $"{pv:0.0} kWh", "odhad z posledných dní", pv >= consumption * 0.75 ? "good" : "neutral"),
                new ForecastMetric("Spotreba", $"{consumption:0.0} kWh", "očakávaný denný odber domácnosti", consumption <= pv ? "good" : "warning"),
                new ForecastMetric("Import", $"{import:0.0} kWh", "Riziko odberu zo siete", import <= 2 ? "good" : "warning"),
                new ForecastMetric("Export", $"{export:0.0} kWh", "priestor pre flexibilnú záťaž", export >= 2 ? "accent" : "neutral")
            ]
        };
    }

    private static IReadOnlyList<SystemAnomaly> BuildAnomalies(
        IReadOnlyList<TimeSeriesPoint> timeline,
        IReadOnlyList<DailyHistoryPoint> history,
        BatteryInformation? latestBattery,
        double freshnessMinutes,
        double mpptImbalancePct,
        double averageGridFrequencyHz,
        double peakBatteryTempC,
        double importWindowPct,
        double exportWindowPct)
    {
        var anomalies = new List<SystemAnomaly>();

        if (freshnessMinutes > 20)
        {
            anomalies.Add(new SystemAnomaly(
                "Oneskorená telemetria",
                "Live dáta sú staršie než zdravé prevádzkové okno. Skontrolujte logger, konektivitu meniča alebo ingest do Supabase.",
                "danger",
                "Oneskorenie",
                $"{freshnessMinutes:0.#} min"));
        }

        if (averageGridFrequencyHz > 0 && Math.Abs(averageGridFrequencyHz - 50) > 0.12)
        {
            anomalies.Add(new SystemAnomaly(
                "Odchýlka frekvencie siete",
                "Priemerná frekvencia siete sa dostala mimo bežné úzke pásmo.",
                "warning",
                "Frekvencia",
                $"{averageGridFrequencyHz:0.00} Hz"));
        }

        if (peakBatteryTempC >= 40)
        {
            anomalies.Add(new SystemAnomaly(
                "Vysoká teplota batérie",
                "Špičková teplota batérie je už dosť vysoká na kontrolu odvetrania a umiestnenia.",
                "danger",
                "Teplota batérie",
                $"{peakBatteryTempC:0.0} C"));
        }
        else if (peakBatteryTempC >= 35)
        {
            anomalies.Add(new SystemAnomaly(
                "Batéria sa zohrieva",
                "Teplota batérie je zvýšená oproti komfortnému prevádzkovému pásmu.",
                "warning",
                "Teplota batérie",
                $"{peakBatteryTempC:0.0} C"));
        }

        var latestPv = timeline.LastOrDefault()?.PvKw ?? 0;
        if (latestPv > 1 && mpptImbalancePct > 35)
        {
            anomalies.Add(new SystemAnomaly(
                "Nerovnováha MPPT",
                "Jeden string vyrába citeľne menej než druhý aj keď je FV výroba aktívna.",
                "warning",
                "Rozdiel MPPT",
                $"{mpptImbalancePct:0.#} %"));
        }

        if (timeline.Count > 0)
        {
            var peakImport = timeline.Where(x => x.GridKw < -0.05).Select(x => Math.Abs(x.GridKw)).DefaultIfEmpty().Max();
            var peakExport = timeline.Where(x => x.GridKw > 0.05).Select(x => x.GridKw).DefaultIfEmpty().Max();
            if (peakImport >= 4 || importWindowPct >= 45)
            {
                anomalies.Add(new SystemAnomaly(
                    "Riziko importu",
                    "Systém strávil veľkú časť vybraného okna v importe alebo zaznamenal výrazný importný špičkový odber.",
                    "warning",
                    "Import špička",
                    $"{peakImport:0.00} kW"));
            }

            if (peakExport >= 4 || exportWindowPct >= 45)
            {
                anomalies.Add(new SystemAnomaly(
                    "Nevyužitý FV prebytok",
                    "Dlhšie exportné okno naznačuje priestor pre ohrev vody, nabíjanie EV alebo inú stratégiu ukladania.",
                    "accent",
                    "Export špička",
                    $"{peakExport:0.00} kW"));
            }
        }

        if (latestBattery?.soh is > 0 and < 80)
        {
            anomalies.Add(new SystemAnomaly(
                "Znížené zdravie batérie",
                "Zdravie batérie je pod komfortným prahom pre dlhodobú prevádzku.",
                "warning",
                "SOH",
                $"{latestBattery.soh:0} %"));
        }

        return anomalies
            .OrderBy(x => x.Severity == "danger" ? 0 : x.Severity == "warning" ? 1 : 2)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<OperatorRecommendation> BuildOperatorRecommendations(
        SmartForecastSummary forecast,
        IReadOnlyList<SystemAnomaly> anomalies,
        double todayExport,
        double todayImport,
        double selfConsumptionPct,
        double selfSufficiencyPct,
        int batterySoc,
        double wattUtilizationPct,
        double dailyWattCapturePct)
    {
        var recommendations = new List<OperatorRecommendation>();

        if (forecast.TomorrowExportKwh >= 2.5 || todayExport >= 2.5)
        {
            recommendations.Add(new OperatorRecommendation(
                "Naplánujte spotrebu do solárneho okna",
                "Dnes alebo zajtra sa črtá vyšší export. Presuňte ohrev vody, umývačku, sušičku alebo EV nabíjanie do najsilnejšieho FV okna.",
                $"Možné zníženie exportu o {Math.Max(todayExport, forecast.TomorrowExportKwh):0.0} kWh",
                "good"));
        }

        if ((todayImport >= 3 || forecast.TomorrowImportKwh >= 3) && batterySoc < 45)
        {
            recommendations.Add(new OperatorRecommendation(
                "Chráňte večernú rezervu batérie",
                "Viditeľné je riziko importu a SOC nie je vysoké. Presuňte flexibilné spotrebiče mimo večernej špičky a ponechajte batériu ako rezervu na noc.",
                "Nižšia závislosť od siete",
                "warning"));
        }

        if (todayExport > 1.5 && wattUtilizationPct < 12 && dailyWattCapturePct < 35)
        {
            recommendations.Add(new OperatorRecommendation(
                "Skontrolujte zachytávanie cez WattRouter",
                "Systém exportuje, no využitie WattRoutera je nízke. Overte priority relé, limit teploty bojlera a stav SSR.",
                "Vyššia vlastná spotreba",
                "accent"));
        }

        if (selfConsumptionPct < 70)
        {
            recommendations.Add(new OperatorRecommendation(
                "Zvýšte vlastnú spotrebu",
                "Využite exportné okná pre riaditeľné spotrebiče skôr, než energia odíde do siete.",
                $"Aktuálna vlastná spotreba {selfConsumptionPct:0.#} %",
                "warning"));
        }

        if (selfSufficiencyPct >= 75 && anomalies.All(x => x.Severity != "danger"))
        {
            recommendations.Add(new OperatorRecommendation(
                "Ponechajte aktuálnu stratégiu",
                "Autonómia je silná a nevidno žiadnu kritickú anomáliu. Stačí ďalej sledovať exportné špičky a teplotu batérie.",
                $"Autonómia {selfSufficiencyPct:0.#} %",
                "good"));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new OperatorRecommendation(
                "Sledujte ďalší cyklus",
                "Momentálne sa neukazuje potreba silného zásahu. Ďalšiu presnosť prinesie viac histórie pre budúcu predikciu.",
                "Nízke riziko",
                "neutral"));
        }

        return recommendations.Take(4).ToArray();
    }

    private static IReadOnlyList<EnergyBreakdownBar> BuildEnergyBreakdowns(
        double todayPv,
        double todayConsumption,
        double todayExport,
        double todayImport,
        double todayBatteryCharge,
        double todayBatteryDischarge,
        double lifetimeSelfUse,
        double lifetimeSelfSufficiency,
        double lifetimeImport,
        double lifetimeExport)
    {
        var directSolarUse = Math.Max(0, todayPv - todayExport - todayBatteryCharge);
        var solarToLoad = Math.Max(0, todayConsumption - todayImport - todayBatteryDischarge);

        return
        [
            new EnergyBreakdownBar
            {
                Title = "Dnešná výroba",
                TotalKwh = Math.Round(todayPv, 1),
                Segments = NormalizeSegments(
                    todayPv,
                    [
                        new EnergyBarSegment("Priamo spotrebované", Math.Round(directSolarUse, 1), "solar"),
                        new EnergyBarSegment("Nabíjanie batérie", Math.Round(todayBatteryCharge, 1), "battery"),
                        new EnergyBarSegment("Export do siete", Math.Round(todayExport, 1), "grid")
                    ])
            },
            new EnergyBreakdownBar
            {
                Title = "Dnešná spotreba",
                TotalKwh = Math.Round(todayConsumption, 1),
                Segments = NormalizeSegments(
                    todayConsumption,
                    [
                        new EnergyBarSegment("Pokryté z FV", Math.Round(solarToLoad, 1), "solar"),
                        new EnergyBarSegment("Vybitie batérie", Math.Round(todayBatteryDischarge, 1), "battery"),
                        new EnergyBarSegment("Nákup zo siete", Math.Round(todayImport, 1), "import")
                    ])
            },
            new EnergyBreakdownBar
            {
                Title = "Celoživotný stav",
                TotalKwh = Math.Round(lifetimeSelfUse + lifetimeImport + lifetimeExport, 1),
                Segments = NormalizeSegments(
                    lifetimeSelfUse + lifetimeImport + lifetimeExport,
                    [
                        new EnergyBarSegment("Lokálne pokrytie", Math.Round(lifetimeSelfSufficiency, 1), "good"),
                        new EnergyBarSegment("Import", Math.Round(lifetimeImport, 1), "import"),
                        new EnergyBarSegment("Export", Math.Round(lifetimeExport, 1), "grid")
                    ])
            }
        ];
    }

    private static IReadOnlyList<EnergyBarSegment> NormalizeSegments(double totalKwh, IReadOnlyList<EnergyBarSegment> segments)
    {
        if (totalKwh <= 0 || segments.Count == 0)
        {
            return segments;
        }

        var currentTotal = segments.Sum(x => Math.Max(0, x.ValueKwh));
        if (Math.Abs(currentTotal - totalKwh) <= 0.15)
        {
            return segments;
        }

        var normalized = new List<EnergyBarSegment>(segments.Count);
        var correction = Math.Max(0, Math.Round(totalKwh - currentTotal, 1));

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            normalized.Add(i == 0
                ? segment with { ValueKwh = Math.Round(segment.ValueKwh + correction, 1) }
                : segment with { ValueKwh = Math.Round(Math.Max(0, segment.ValueKwh), 1) });
        }

        return normalized;
    }

    private static IReadOnlyList<DailyEnergyStory> BuildDailyStories(
        IReadOnlyList<DailyHistoryPoint> history,
        DateTime localToday,
        double todayPv,
        double todayConsumption,
        double todayImport,
        double todayExport,
        double todaySelfConsumptionPct,
        double todaySelfSufficiencyPct)
    {
        var stories = new List<DailyEnergyStory>(2);
        var ordered = history
            .Select(point => new
            {
                Point = point,
                Date = DateTime.TryParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.Date
                    : (DateTime?)null
            })
            .Where(item => item.Date.HasValue)
            .ToArray();

        var todayPoint = ordered.FirstOrDefault(item => item.Date == localToday)?.Point
            ?? new DailyHistoryPoint(
                localToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                localToday.ToString("dd.MM."),
                Math.Round(todayPv, 1),
                Math.Round(todayConsumption, 1),
                Math.Round(todayImport, 1),
                Math.Round(todayExport, 1),
                0,
                0,
                Math.Round(Math.Max(0, todayConsumption - todayImport), 1),
                Math.Round(Math.Max(0, todayPv - todayExport), 1),
                Math.Round(todaySelfConsumptionPct, 1),
                Math.Round(Math.Max(0, todayConsumption - todayImport), 1),
                Math.Round(todaySelfSufficiencyPct, 1));

        var yesterdayPoint = ordered.FirstOrDefault(item => item.Date == localToday.AddDays(-1))?.Point;
        var previousPoint = ordered
            .Where(item => item.Date < localToday)
            .OrderByDescending(item => item.Date)
            .Select(item => item.Point)
            .FirstOrDefault();

        stories.Add(BuildDailyStory(todayPoint, "Dnes", previousPoint));
        if (yesterdayPoint is not null)
        {
            var beforeYesterday = ordered
                .Where(item => item.Date < localToday.AddDays(-1))
                .OrderByDescending(item => item.Date)
                .Select(item => item.Point)
                .FirstOrDefault();

            stories.Add(BuildDailyStory(yesterdayPoint, "Včera", beforeYesterday));
        }

        return stories;
    }

    private static DailyEnergyStory BuildDailyStory(DailyHistoryPoint point, string dayLabel, DailyHistoryPoint? previousPoint)
    {
        var balance = point.Export - point.Import;
        var tone = point.SelfSufficiencyPct >= 75 && balance >= 0
            ? "good"
            : point.SelfSufficiencyPct >= 55
                ? "accent"
                : "warning";

        var headline = point.Pv >= point.Consumption && balance >= 0
            ? "Dom fungoval prevažne z vlastnej energie s rezervou"
            : point.SelfSufficiencyPct >= 70
                ? "Výroba pokryla väčšinu spotreby"
                : "Sieť musela doplniť časť spotreby";

        var balanceSentence = balance >= 0
            ? $"Do siete sa dostal prebytok {balance:0.0} kWh."
            : $"Zo siete bolo potrebné dokúpiť o {Math.Abs(balance):0.0} kWh viac, než sa vyexportovalo.";

        double? previousPvDelta = null;
        var comparisonSentence = string.Empty;
        if (previousPoint is not null)
        {
            var deltaPv = point.Pv - previousPoint.Pv;
            previousPvDelta = Math.Round(deltaPv, 1);
            if (Math.Abs(deltaPv) >= 0.5)
            {
                comparisonSentence = deltaPv > 0
                    ? $" Oproti predchádzajúcemu dňu bola výroba vyššia o {deltaPv:0.0} kWh."
                    : $" Oproti predchádzajúcemu dňu bola výroba nižšia o {Math.Abs(deltaPv):0.0} kWh.";
            }
        }

        var summary = $"{dayLabel} systém vyrobil {point.Pv:0.0} kWh a domácnosť spotrebovala {point.Consumption:0.0} kWh. {balanceSentence}{comparisonSentence}";

        return new DailyEnergyStory
        {
            Key = point.Date,
            Date = DateTime.ParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DayLabel = $"{dayLabel} · {point.Label}",
            Headline = headline,
            Summary = summary,
            Tone = tone,
            BalanceLabel = $"{(balance >= 0 ? "+" : string.Empty)}{balance:0.0} kWh",
            AutonomyLabel = $"{point.SelfSufficiencyPct:0.0} %",
            SelfUseLabel = $"{point.SelfUsePct:0.0} %",
            PvKwh = Math.Round(point.Pv, 1),
            ConsumptionKwh = Math.Round(point.Consumption, 1),
            ImportKwh = Math.Round(point.Import, 1),
            ExportKwh = Math.Round(point.Export, 1),
            BalanceKwh = Math.Round(balance, 1),
            SelfSufficiencyPct = Math.Round(point.SelfSufficiencyPct, 1),
            SelfUsePct = Math.Round(point.SelfUsePct, 1),
            PreviousPvDeltaKwh = previousPvDelta,
            Highlights =
            [
                $"FV výroba {point.Pv:0.0} kWh",
                $"Spotreba domácnosti {point.Consumption:0.0} kWh",
                point.Import > 0 ? $"Import {point.Import:0.0} kWh" : "Import bol prakticky nulový",
                point.Export > 0 ? $"Export {point.Export:0.0} kWh" : "Export bez výraznejšieho prebytku"
            ]
        };
    }

    private DashboardChartPayload BuildCharts(
        IReadOnlyList<WattRouterInfo> wattPoints,
        IReadOnlyList<double> wattPowerSeries,
        IReadOnlyList<PvInformation> pvPoints,
        IReadOnlyList<BatteryInformation> batteryPoints,
        IReadOnlyList<GridInformation> gridPoints,
        IReadOnlyList<DailyHistoryPoint> history,
        IReadOnlyList<DailyTotalPoint> totalHistory,
        SystemProfile systemProfile,
        int relayChannelCount)
    {
        var pvCarryForwardWindow = TimeSpan.FromMinutes(5);
        var pvSeries = pvPoints
            .GroupBy(x => x.created_at)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.mppt).ToArray();
                var mppt1 = ordered.FirstOrDefault(x => x.mppt == 1);
                var mppt2 = ordered.FirstOrDefault(x => x.mppt == 2);

                if (mppt1 is null && mppt2 is null)
                {
                    mppt1 = ordered.ElementAtOrDefault(0);
                    mppt2 = ordered.ElementAtOrDefault(1);
                }
                return new PvChartSample(
                    group.Key,
                    ordered.Sum(x => (double)(x.power ?? 0)),
                    mppt1?.power,
                    mppt2?.power,
                    ordered.Where(x => x.voltage.HasValue).Select(x => (double)x.voltage!.Value).DefaultIfEmpty().Average(),
                    ordered.Where(x => x.current.HasValue).Select(x => (double)x.current!.Value).DefaultIfEmpty().Average(),
                    mppt1?.voltage,
                    mppt2?.voltage,
                    mppt1?.current,
                    mppt2?.current);
            })
            .ToArray();

        var gridSeries = gridPoints.OrderBy(x => x.created_at).ToArray();
        var batterySeries = batteryPoints.OrderBy(x => x.created_at).ToArray();
        var wattSeries = wattPoints
            .Select((point, index) => new WattChartSample(point.created_at, wattPowerSeries.ElementAtOrDefault(index), point))
            .OrderBy(x => x.Time)
            .ToArray();

        var timestamps = new HashSet<DateTimeOffset>(wattSeries.Select(x => x.Time));
        foreach (var timestamp in pvSeries.Select(x => x.Time))
        {
            timestamps.Add(timestamp);
        }

        foreach (var timestamp in gridSeries.Select(x => x.created_at))
        {
            timestamps.Add(timestamp);
        }

        foreach (var timestamp in batterySeries.Select(x => x.created_at))
        {
            timestamps.Add(timestamp);
        }

        var orderedTimes = timestamps.OrderBy(x => x).ToArray();
        var timeline = new List<TimeSeriesPoint>(orderedTimes.Length);

        var pvIndex = 0;
        var gridIndex = 0;
        var batteryIndex = 0;
        var wattIndex = 0;

        PvChartSample? currentPv = null;
        PvChannelSnapshot? currentMppt1 = null;
        PvChannelSnapshot? currentMppt2 = null;
        GridInformation? currentGrid = null;
        BatteryInformation? currentBattery = null;
        WattChartSample? currentWatt = null;

        foreach (var time in orderedTimes)
        {
            while (pvIndex < pvSeries.Length && pvSeries[pvIndex].Time <= time)
            {
                currentPv = pvSeries[pvIndex++];

                if (currentPv.Mppt1.HasValue || currentPv.Mppt1Voltage.HasValue || currentPv.Mppt1Current.HasValue)
                {
                    currentMppt1 = new PvChannelSnapshot(
                        currentPv.Time,
                        currentPv.Mppt1 ?? 0,
                        currentPv.Mppt1Voltage ?? 0,
                        currentPv.Mppt1Current ?? 0);
                }

                if (currentPv.Mppt2.HasValue || currentPv.Mppt2Voltage.HasValue || currentPv.Mppt2Current.HasValue)
                {
                    currentMppt2 = new PvChannelSnapshot(
                        currentPv.Time,
                        currentPv.Mppt2 ?? 0,
                        currentPv.Mppt2Voltage ?? 0,
                        currentPv.Mppt2Current ?? 0);
                }
            }

            while (gridIndex < gridSeries.Length && gridSeries[gridIndex].created_at <= time)
            {
                currentGrid = gridSeries[gridIndex++];
            }

            while (batteryIndex < batterySeries.Length && batterySeries[batteryIndex].created_at <= time)
            {
                currentBattery = batterySeries[batteryIndex++];
            }

            while (wattIndex < wattSeries.Length && wattSeries[wattIndex].Time <= time)
            {
                currentWatt = wattSeries[wattIndex++];
            }

            var displayTime = ToDashboardDisplayTime(time);
            var local = displayTime;
            var gridKw = currentGrid?.active_power_pcc_total ?? 0;
            var inverterKw = currentGrid?.active_power_output_total ?? 0;
            var batteryKw = currentBattery?.power ?? 0;
            var mppt1Fresh = currentMppt1 is not null && time - currentMppt1.Time <= pvCarryForwardWindow;
            var mppt2Fresh = currentMppt2 is not null && time - currentMppt2.Time <= pvCarryForwardWindow;
            var mppt1Kw = mppt1Fresh ? currentMppt1!.PowerKw : 0;
            var mppt2Kw = mppt2Fresh ? currentMppt2!.PowerKw : 0;
            var mppt1Voltage = mppt1Fresh ? currentMppt1!.VoltageV : 0;
            var mppt2Voltage = mppt2Fresh ? currentMppt2!.VoltageV : 0;
            var mppt1Current = mppt1Fresh ? currentMppt1!.CurrentA : 0;
            var mppt2Current = mppt2Fresh ? currentMppt2!.CurrentA : 0;
            var pvKw = mppt1Kw + mppt2Kw;
            var activePvVoltages = new List<double>(2);
            var activePvCurrents = new List<double>(2);
            if (mppt1Fresh)
            {
                activePvVoltages.Add(mppt1Voltage);
                activePvCurrents.Add(mppt1Current);
            }

            if (mppt2Fresh)
            {
                activePvVoltages.Add(mppt2Voltage);
                activePvCurrents.Add(mppt2Current);
            }

            var pvVoltage = activePvVoltages.Count > 0 ? activePvVoltages.Average() : 0;
            var pvCurrent = activePvCurrents.Count > 0 ? activePvCurrents.Average() : 0;
            var consumptionKw = ComputeConsumptionKw(gridKw, inverterKw, pvKw, batteryKw);
            var pvSaturationPct = systemProfile.InstalledPvKw > 0 ? Math.Round(pvKw / systemProfile.InstalledPvKw * 100, 1) : 0;
            var wattUtilizationPct = ComputeWattUtilizationPct(currentWatt?.Point, relayChannelCount);

            timeline.Add(new TimeSeriesPoint(
                displayTime.ToUnixTimeMilliseconds(),
                local.ToString("HH:mm"),
                Math.Round(pvKw, 2),
                Math.Round(consumptionKw, 2),
                Math.Round(gridKw, 2),
                Math.Round(batteryKw, 2),
                Math.Round(inverterKw, 2),
                Math.Round(currentWatt?.PowerKw ?? 0, 2),
                Math.Round((double)(currentBattery?.soc ?? 0), 1),
                pvSaturationPct,
                wattUtilizationPct,
                Math.Round(currentWatt?.Point.GetAverageChannelLoadPercentage(relayChannelCount) ?? 0, 1),
                currentWatt?.Point.ActiveChannelCount ?? 0,
                Math.Round(mppt1Kw, 2),
                Math.Round(mppt2Kw, 2),
                currentWatt?.Point.gridFetch ?? false,
                Math.Round(pvVoltage, 1),
                Math.Round(pvCurrent, 1),
                Math.Round(mppt1Voltage, 1),
                Math.Round(mppt2Voltage, 1),
                Math.Round(mppt1Current, 1),
                Math.Round(mppt2Current, 1),
                Math.Round(currentBattery?.voltage ?? 0, 1),
                Math.Round(currentBattery?.current ?? 0, 1),
                Math.Round(currentBattery?.temperature ?? 0, 1),
                Math.Round((double)(currentBattery?.soh ?? 0), 1),
                Math.Round((double)(currentBattery?.charge_cycle ?? 0), 1),
                Math.Round(currentGrid?.voltage_phase_r ?? 0, 1),
                Math.Round(currentGrid?.voltage_phase_s ?? 0, 1),
                Math.Round(currentGrid?.voltage_phase_t ?? 0, 1),
                Math.Round(currentGrid?.current_output_r ?? 0, 1),
                Math.Round(currentGrid?.current_output_s ?? 0, 1),
                Math.Round(currentGrid?.current_output_t ?? 0, 1),
                Math.Round(currentGrid?.active_power_output_r ?? 0, 2),
                Math.Round(currentGrid?.active_power_output_s ?? 0, 2),
                Math.Round(currentGrid?.active_power_output_t ?? 0, 2),
                Math.Round(currentGrid?.current_pcc_r ?? 0, 1),
                Math.Round(currentGrid?.current_pcc_s ?? 0, 1),
                Math.Round(currentGrid?.current_pcc_t ?? 0, 1),
                Math.Round(currentGrid?.active_power_pcc_r ?? 0, 2),
                Math.Round(currentGrid?.active_power_pcc_s ?? 0, 2),
                Math.Round(currentGrid?.active_power_pcc_t ?? 0, 2),
                Math.Round(currentGrid?.grid_frequency ?? 0, 2)));
        }

        return new DashboardChartPayload
        {
            Timeline = timeline,
            History = history,
            Totals = totalHistory
        };
    }

    private sealed record SystemFreshnessRow(string sn_number, DateTimeOffset created_at);

    private sealed record SystemStatsFreshnessRow(string sn_number, DateTimeOffset timestamp);

    private sealed record CacheEntry<T>(DateTimeOffset CreatedUtc, T Value);

    private sealed record PvChartSample(
        DateTimeOffset Time,
        double Total,
        double? Mppt1,
        double? Mppt2,
        double AverageVoltage,
        double AverageCurrent,
        double? Mppt1Voltage,
        double? Mppt2Voltage,
        double? Mppt1Current,
        double? Mppt2Current);

    private sealed record PvChannelSnapshot(
        DateTimeOffset Time,
        double PowerKw,
        double VoltageV,
        double CurrentA);

    private sealed record WattChartSample(DateTimeOffset Time, double PowerKw, WattRouterInfo Point);

    private sealed record DailyStatsSample(
        StatisticalInformation Row,
        DateTime LocalTime,
        double CounterTotal);

    private sealed record DailyStatsDay(
        DateTime LocalDate,
        StatisticalInformation LatestRow,
        DailyStatsAccumulator Values)
    {
        public double Pv => Values.Pv;
        public double Consumption => Values.Consumption;
        public double Import => Values.Import;
        public double Export => Values.Export;
        public double BatteryCharge => Values.BatteryCharge;
        public double BatteryDischarge => Values.BatteryDischarge;

        public DailyStatsDay Merge(DailyStatsDay other)
        {
            var latestRow = other.LatestRow.timestamp > LatestRow.timestamp ? other.LatestRow : LatestRow;
            return this with
            {
                LatestRow = latestRow,
                Values = Values.Merge(other.Values)
            };
        }
    }

    private sealed record DailyStatsAccumulator
    {
        public double Pv { get; init; }
        public double Consumption { get; init; }
        public double Import { get; init; }
        public double Export { get; init; }
        public double BatteryCharge { get; init; }
        public double BatteryDischarge { get; init; }

        public DailyStatsAccumulator Merge(DailyStatsAccumulator other)
        {
            return new DailyStatsAccumulator
            {
                Pv = Math.Max(Pv, other.Pv),
                Consumption = Math.Max(Consumption, other.Consumption),
                Import = Math.Max(Import, other.Import),
                Export = Math.Max(Export, other.Export),
                BatteryCharge = Math.Max(BatteryCharge, other.BatteryCharge),
                BatteryDischarge = Math.Max(BatteryDischarge, other.BatteryDischarge)
            };
        }
    }
}

