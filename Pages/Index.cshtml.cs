using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NestStats2.Data;
using NestStats2.Models;
using NestStats2.Services;

namespace NestStats2.Pages;

public sealed class IndexModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PreferenceCookieLifetime = TimeSpan.FromDays(180);

    private readonly ApplicationDbContext _dbContext;
    private readonly IEnergyDashboardService _dashboardService;
    private readonly IDashboardLoadCoordinator _dashboardLoadCoordinator;
    private readonly IWeatherForecastService _weatherForecastService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext dbContext,
        IEnergyDashboardService dashboardService,
        IDashboardLoadCoordinator dashboardLoadCoordinator,
        IWeatherForecastService weatherForecastService,
        UserManager<ApplicationUser> userManager,
        ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _dashboardService = dashboardService;
        _dashboardLoadCoordinator = dashboardLoadCoordinator;
        _weatherForecastService = weatherForecastService;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? SnNumber { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? Day { get; set; }

    [BindProperty(SupportsGet = true)]
    public int HoursBack { get; set; } = 24;

    [BindProperty(SupportsGet = true)]
    public string? LoadToken { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool QuietRefresh { get; set; }

    public DashboardData Dashboard { get; private set; } = new();

    public string DashboardJson { get; private set; } = "{}";

    public bool IsAuthenticated { get; private set; }

    public bool IsAdmin { get; private set; }

    public int AssignedSystemCount { get; private set; }

    public string CurrentUserLabel { get; private set; } = string.Empty;

    public string DashboardPreferenceCookieName { get; private set; } = string.Empty;

    public bool CanShowDashboard => IsAuthenticated && (IsAdmin || AssignedSystemCount > 0);

    public bool HasSystem => Dashboard.Systems.Count > 0 && !string.IsNullOrWhiteSpace(Dashboard.SelectedSnNumber);

    public string SelectedDayValue => Day?.ToString("yyyy-MM-dd") ?? string.Empty;

    public string RangeLabel =>
        $"{Dashboard.RangeStartLocal:dd.MM.yyyy HH:mm} — {Dashboard.RangeEndLocal:dd.MM.yyyy HH:mm}";

    public string LastSyncLabel =>
        Dashboard.InitialLive is null
            ? "Bez live telemetrie"
            : TimeZoneInfo.ConvertTime(Dashboard.InitialLive.time, TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm:ss");

    public bool IsDataFresh => Dashboard.InitialLive is not null &&
        (DateTimeOffset.UtcNow - Dashboard.InitialLive.time).TotalMinutes < 5;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsAuthenticated = User.Identity?.IsAuthenticated == true;
            IsAdmin = User.IsInRole(IdentitySeeder.AdminRole);

            if (!IsAuthenticated)
            {
                Dashboard = new DashboardData();
                DashboardJson = "{}";
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                Dashboard = new DashboardData();
                DashboardJson = "{}";
                return Page();
            }

            CurrentUserLabel = string.IsNullOrWhiteSpace(user.DisplayName)
                ? (user.Email ?? user.UserName ?? "Používateľ")
                : user.DisplayName;

            DashboardPreferenceCookieName = BuildDashboardPreferenceCookieName(user.Id);
            var savedPreferences = ReadDashboardPreferenceCookie(DashboardPreferenceCookieName);
            var hasUserChanges = false;
            var nowUtc = DateTimeOffset.UtcNow;

            if (user.LastSeenUtc is null || nowUtc - user.LastSeenUtc.Value > TimeSpan.FromMinutes(5))
            {
                user.LastSeenUtc = nowUtc;
                hasUserChanges = true;
            }

            var assignedSystems = IsAdmin
                ? new List<string>()
                : await _dbContext.UserEnergySystems
                    .AsNoTracking()
                    .Where(x => x.UserId == user.Id)
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.SystemName)
                    .Select(x => x.SnNumber)
                    .ToListAsync(cancellationToken);
            var assignedSystemSet = IsAdmin
                ? null
                : new HashSet<string>(assignedSystems, StringComparer.OrdinalIgnoreCase);

            AssignedSystemCount = IsAdmin ? int.MaxValue : assignedSystems.Count;

            if (!IsAdmin)
            {
                var hasPreferredSystem = assignedSystemSet!.Contains(user.PreferredSystemSn);
                if (!hasPreferredSystem && assignedSystems.Count > 0)
                {
                    user.PreferredSystemSn = assignedSystems[0];
                    hasPreferredSystem = true;
                    hasUserChanges = true;
                }

                DateTimeOffset? onboardingCompletedUtc = hasPreferredSystem
                    ? user.OnboardingCompletedUtc ?? nowUtc
                    : null;

                if (user.OnboardingCompletedUtc != onboardingCompletedUtc)
                {
                    user.OnboardingCompletedUtc = onboardingCompletedUtc;
                    hasUserChanges = true;
                }

                if (!hasPreferredSystem)
                {
                    if (hasUserChanges)
                    {
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }

                    return RedirectToPage("/Onboarding/Index");
                }
            }

            if (!CanShowDashboard)
            {
                if (hasUserChanges)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                Dashboard = new DashboardData();
                DashboardJson = "{}";
                return Page();
            }

            if (!Request.Query.ContainsKey(nameof(SnNumber)) &&
                !string.IsNullOrWhiteSpace(savedPreferences?.SystemSn))
            {
                SnNumber = savedPreferences.SystemSn;
            }

            if (!Request.Query.ContainsKey(nameof(Day)) &&
                DateTime.TryParse(savedPreferences?.Day, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var savedDay))
            {
                Day = savedDay.Date;
            }

            if (!Request.Query.ContainsKey(nameof(HoursBack)) &&
                savedPreferences?.HoursBack is > 0)
            {
                HoursBack = savedPreferences.HoursBack.Value;
            }

            var requestedSn = SnNumber;
            if (!IsAdmin &&
                !string.IsNullOrWhiteSpace(requestedSn) &&
                !assignedSystemSet!.Contains(requestedSn))
            {
                requestedSn = assignedSystems.FirstOrDefault();
            }

            var preparedDashboard = !string.IsNullOrWhiteSpace(LoadToken)
                ? _dashboardLoadCoordinator.TryGetPreparedDashboard(LoadToken, user.Id)
                : null;

            Dashboard = preparedDashboard ?? await _dashboardService.GetDashboardAsync(
                requestedSn,
                Day,
                HoursBack,
                IsAdmin ? null : assignedSystems,
                cancellationToken: cancellationToken);

            Dashboard.Weather = await _weatherForecastService.GetForecastAsync(
                Dashboard.SystemAddress,
                Dashboard.InstalledPvKw,
                cancellationToken);

            Dashboard.Weather = ReconcileWeatherForecastWithTelemetry(Dashboard);
            Dashboard.EveningImport = BuildEveningImportPrediction(Dashboard);

            SnNumber = Dashboard.SelectedSnNumber;
            HoursBack = Dashboard.HoursBack;
            if (!string.Equals(user.PreferredSystemSn, Dashboard.SelectedSnNumber, StringComparison.OrdinalIgnoreCase))
            {
                user.PreferredSystemSn = Dashboard.SelectedSnNumber;
                hasUserChanges = true;
            }

            var bootstrap = new DashboardBootstrap
            {
                LiveEndpoint = Url.Page("/Index", "Live", new { snNumber = Dashboard.SelectedSnNumber }) ?? string.Empty,
                HistoryEndpoint = Url.Page("/Index", "History", new
                {
                    snNumber = Dashboard.SelectedSnNumber,
                    day = Dashboard.SelectedDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }) ?? string.Empty,
                LoadStartEndpoint = Url.Page("/Index", "StartLoad") ?? string.Empty,
                LoadProgressEndpoint = Url.Page("/Index", "LoadProgress") ?? string.Empty,
                RefreshSeconds = 15,
                WattMaxKw = Dashboard.WattMaxKw,
                InstalledPvKw = Dashboard.InstalledPvKw,
                BatteryCapacityKwh = Dashboard.BatteryCapacityKwh,
                RelayChannelCount = Dashboard.RelayChannelCount,
                Charts = Dashboard.Charts,
                InitialLive = Dashboard.InitialLive,
                EnergyBreakdowns = Dashboard.EnergyBreakdowns,
                SmartForecast = Dashboard.SmartForecast,
                Anomalies = Dashboard.Anomalies,
                OperatorRecommendations = Dashboard.OperatorRecommendations,
                Weather = Dashboard.Weather,
                EveningImport = Dashboard.EveningImport,
                EnvironmentalBenefits = Dashboard.EnvironmentalBenefits,
                SpotMarket = Dashboard.SpotMarket,
                DailyStories = Dashboard.DailyStories
            };

            WriteDashboardPreferenceCookie(
                DashboardPreferenceCookieName,
                (savedPreferences ?? new DashboardPreferenceState()) with
                {
                    Version = 1,
                    SystemSn = Dashboard.SelectedSnNumber,
                    Day = Dashboard.SelectedDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        ?? Day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    HoursBack = Dashboard.HoursBack,
                    UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                });

            DashboardJson = JsonSerializer.Serialize(bootstrap, JsonOptions);
            if (hasUserChanges)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build dashboard page");
            ModelState.AddModelError(string.Empty, "Dashboard sa nepodarilo načítať.");
            Dashboard = new DashboardData();
            DashboardJson = "{}";
            return Page();
        }
    }

    public async Task<IActionResult> OnGetLiveAsync(string snNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snNumber))
            return BadRequest();

        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var isAdmin = await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole);
        if (!isAdmin)
        {
            var hasAccess = await _dbContext.UserEnergySystems.AsNoTracking().AnyAsync(
                x => x.UserId == user.Id && x.SnNumber == snNumber,
                cancellationToken);

            if (!hasAccess)
                return Forbid();
        }

        var live = await _dashboardService.GetLiveSnapshotAsync(snNumber, cancellationToken);
        if (live is null)
        {
            return new JsonResult(new LiveSnapshot
            {
                time = DateTimeOffset.UtcNow,
                relayCount = 0
            });
        }

        return new JsonResult(live);
    }

    public async Task<IActionResult> OnGetHistoryAsync(
        string snNumber,
        string? window,
        DateTime? day,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snNumber))
            return BadRequest();

        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var isAdmin = await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole);
        IReadOnlyCollection<string>? allowedSystems = null;

        if (!isAdmin)
        {
            var assignedSystems = await _dbContext.UserEnergySystems
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .Select(x => x.SnNumber)
                .ToListAsync(cancellationToken);

            if (!assignedSystems.Contains(snNumber, StringComparer.OrdinalIgnoreCase))
                return Forbid();

            allowedSystems = assignedSystems;
        }

        var days = NormalizeHistoryWindow(window);
        var history = await _dashboardService.GetHistoryAsync(
            snNumber,
            day,
            days,
            allowedSystems,
            cancellationToken);

        return new JsonResult(new { history });
    }

    public async Task<IActionResult> OnGetStartLoadAsync(
        string? snNumber,
        DateTime? day,
        int hoursBack,
        bool quietRefresh,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole);
        var assignedSystems = isAdmin
            ? new List<string>()
            : await _dbContext.UserEnergySystems
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SystemName)
                .Select(x => x.SnNumber)
                .ToListAsync(cancellationToken);
        var assignedSystemSet = isAdmin
            ? null
            : new HashSet<string>(assignedSystems, StringComparer.OrdinalIgnoreCase);

        if (!isAdmin && assignedSystems.Count == 0)
        {
            return Forbid();
        }

        var requestedSn = string.IsNullOrWhiteSpace(snNumber) ? null : snNumber.Trim();
        if (!isAdmin &&
            !string.IsNullOrWhiteSpace(requestedSn) &&
            !assignedSystemSet!.Contains(requestedSn))
        {
            requestedSn = assignedSystems.FirstOrDefault();
        }

        var normalizedHoursBack = NormalizeRequestedHoursBack(hoursBack);
        var jobId = _dashboardLoadCoordinator.StartJob(new DashboardLoadRequest(
            user.Id,
            requestedSn,
            day,
            normalizedHoursBack,
            isAdmin ? null : assignedSystems,
            isAdmin));

        var navigateUrl = Url.Page("/Index", null, new
        {
            SnNumber = requestedSn,
            Day = day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            HoursBack = normalizedHoursBack,
            LoadToken = jobId,
            QuietRefresh = quietRefresh ? true : (bool?)null
        }) ?? Url.Page("/Index") ?? "/";

        return new JsonResult(new
        {
            jobId,
            navigateUrl
        });
    }

    public async Task<IActionResult> OnGetLoadProgressAsync(string jobId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return BadRequest();
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var snapshot = _dashboardLoadCoordinator.GetSnapshot(jobId, user.Id);
        if (snapshot is null)
        {
            return NotFound();
        }

        return new JsonResult(snapshot);
    }

    public async Task<IActionResult> OnGetExportAsync(
        string? snNumber,
        DateTime? day,
        int hoursBack,
        string? dataset,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole);
        var assignedSystems = isAdmin
            ? new List<string>()
            : await _dbContext.UserEnergySystems
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SystemName)
                .Select(x => x.SnNumber)
                .ToListAsync(cancellationToken);
        var assignedSystemSet = isAdmin
            ? null
            : new HashSet<string>(assignedSystems, StringComparer.OrdinalIgnoreCase);

        if (!isAdmin && assignedSystems.Count == 0)
        {
            return Forbid();
        }

        var normalizedSn = string.IsNullOrWhiteSpace(snNumber)
            ? null
            : snNumber.Trim();

        if (!isAdmin &&
            !string.IsNullOrWhiteSpace(normalizedSn) &&
            !assignedSystemSet!.Contains(normalizedSn))
        {
            return Forbid();
        }

        var dashboard = await _dashboardService.GetDashboardAsync(
            normalizedSn,
            day,
            hoursBack,
            isAdmin ? null : assignedSystems,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(dashboard.SelectedSnNumber))
        {
            return NotFound();
        }

        var normalizedDataset = string.Equals(dataset, "timeline", StringComparison.OrdinalIgnoreCase)
            ? "timeline"
            : "history";
        var csv = normalizedDataset == "timeline"
            ? BuildTimelineCsv(dashboard)
            : BuildHistoryCsv(dashboard);
        var dayPart = dashboard.SelectedDay?.ToString("yyyy-MM-dd") ?? dashboard.RangeEndLocal.ToString("yyyy-MM-dd");
        var fileName = $"neststats-{dashboard.SelectedSnNumber}-{normalizedDataset}-{dayPart}.csv";

        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    private static string BuildHistoryCsv(DashboardData dashboard)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,SystemName,SnNumber,PvKwh,ConsumptionKwh,ImportKwh,ExportKwh,BatteryChargeKwh,BatteryDischargeKwh,SelfUseKwh,SelfUsePct,SelfSufficiencyKwh,SelfSufficiencyPct");

        IEnumerable<DailyHistoryPoint> rows = dashboard.History.OrderBy(x => x.Date);
        if (dashboard.SelectedDay.HasValue)
        {
            var selectedDate = dashboard.SelectedDay.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            rows = rows.Where(x => string.Equals(x.Date, selectedDate, StringComparison.Ordinal));
        }

        foreach (var row in rows)
        {
            AppendCsvRow(
                sb,
                row.Date,
                dashboard.SystemName,
                dashboard.SelectedSnNumber,
                FormatNumber(row.Pv),
                FormatNumber(row.Consumption),
                FormatNumber(row.Import),
                FormatNumber(row.Export),
                FormatNumber(row.BatteryCharge),
                FormatNumber(row.BatteryDischarge),
                FormatNumber(row.SelfUseKwh),
                FormatNumber(row.SelfUsePct),
                FormatNumber(row.SelfSufficiencyKwh),
                FormatNumber(row.SelfSufficiencyPct));
        }

        return sb.ToString();
    }

    private static string BuildTimelineCsv(DashboardData dashboard)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LocalTime,SystemName,SnNumber,PvKw,ConsumptionKw,GridKw,BatteryKw,InverterKw,WattKw,SoC,PvSaturationPct,WattUtilizationPct,RelayAverageLoadPct,RelaysOn,Mppt1Kw,Mppt2Kw,GridFetch,PvVoltageV,PvCurrentA,BatteryVoltageV,BatteryCurrentA,BatteryTemperatureC,GridFrequencyHz");

        foreach (var row in dashboard.Charts.Timeline)
        {
            var localTime = DateTimeOffset.FromUnixTimeMilliseconds(row.Ts).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            AppendCsvRow(
                sb,
                localTime,
                dashboard.SystemName,
                dashboard.SelectedSnNumber,
                FormatNumber(row.PvKw),
                FormatNumber(row.ConsumptionKw),
                FormatNumber(row.GridKw),
                FormatNumber(row.BatteryKw),
                FormatNumber(row.InverterKw),
                FormatNumber(row.WattKw),
                FormatNumber(row.SoC),
                FormatNumber(row.PvSaturationPct),
                FormatNumber(row.WattUtilizationPct),
                FormatNumber(row.RelayAverageLoadPct),
                row.RelaysOn.ToString(CultureInfo.InvariantCulture),
                FormatNumber(row.Mppt1Kw),
                FormatNumber(row.Mppt2Kw),
                row.GridFetch ? "true" : "false",
                FormatNumber(row.PvVoltageV),
                FormatNumber(row.PvCurrentA),
                FormatNumber(row.BatteryVoltageV),
                FormatNumber(row.BatteryCurrentA),
                FormatNumber(row.BatteryTemperatureC),
                FormatNumber(row.GridFrequencyHz));
        }

        return sb.ToString();
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] values)
    {
        sb.AppendLine(string.Join(",", values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value}\""
            : value;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int NormalizeRequestedHoursBack(int hoursBack)
    {
        return hoursBack switch
        {
            6 or 12 or 24 or 48 or 72 or 168 => hoursBack,
            _ => 24
        };
    }

    private static string BuildDashboardPreferenceCookieName(string? userId)
    {
        var safeUserId = new string((userId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            safeUserId = "user";
        }

        return $"neststats.dashboard.{safeUserId.ToLowerInvariant()}";
    }

    private static int? NormalizeHistoryWindow(string? window)
    {
        return string.Equals(window, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : int.TryParse(window, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days is 7 or 14 or 30
                ? days
                : 7;
    }

    private DashboardPreferenceState? ReadDashboardPreferenceCookie(string cookieName)
    {
        if (string.IsNullOrWhiteSpace(cookieName) ||
            !Request.Cookies.TryGetValue(cookieName, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        try
        {
            var json = Uri.UnescapeDataString(rawValue);
            return JsonSerializer.Deserialize<DashboardPreferenceState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse dashboard preference cookie {CookieName}", cookieName);
            return null;
        }
    }

    private void WriteDashboardPreferenceCookie(string cookieName, DashboardPreferenceState state)
    {
        if (string.IsNullOrWhiteSpace(cookieName))
        {
            return;
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        Response.Cookies.Append(
            cookieName,
            Uri.EscapeDataString(json),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.Add(PreferenceCookieLifetime),
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private static EveningImportPrediction BuildEveningImportPrediction(DashboardData dashboard)
    {
        var now = DateTime.Now;
        var todayEveningStart = now.Date.AddHours(17);
        var todayEveningEnd = now.Date.AddHours(23).AddMinutes(30);
        var eveningStart = todayEveningStart;
        var eveningEnd = todayEveningEnd;
        var windowLabel = "Dnes 17:00 - 23:30";

        if (now >= todayEveningEnd)
        {
            eveningStart = now.Date.AddDays(1).AddHours(17);
            eveningEnd = now.Date.AddDays(1).AddHours(23).AddMinutes(30);
            windowLabel = "Zajtra 17:00 - 23:30";
        }

        var effectiveEveningStart = now > eveningStart && now < eveningEnd ? now : eveningStart;
        var preEveningStart = now < eveningStart ? now : eveningStart;
        var preEveningEnd = now < eveningStart ? eveningStart : eveningStart;
        var batteryCapacityKwh = Math.Max(0, dashboard.BatteryCapacityKwh);
        var currentSocPct = Math.Clamp(dashboard.LatestBatterySoC, 0, 100);
        var currentBatteryKwh = batteryCapacityKwh * currentSocPct / 100d;

        var preEveningPvKwh = now < eveningStart
            ? EstimateWeatherPvKwh(dashboard.Weather, preEveningStart, preEveningEnd)
            : 0;
        var preEveningConsumptionKwh = now < eveningStart
            ? EstimateConsumptionKwh(dashboard, preEveningStart, preEveningEnd, 0.9)
            : 0;
        var projectedChargeKwh = Math.Min(
            Math.Max(0, batteryCapacityKwh - currentBatteryKwh),
            Math.Max(0, preEveningPvKwh - preEveningConsumptionKwh) * 0.88);
        var projectedBatteryKwh = Math.Min(batteryCapacityKwh, currentBatteryKwh + projectedChargeKwh);
        var projectedSocPct = batteryCapacityKwh > 0 ? projectedBatteryKwh / batteryCapacityKwh * 100 : currentSocPct;

        var eveningConsumptionKwh = EstimateConsumptionKwh(dashboard, effectiveEveningStart, eveningEnd, 1.28);
        var eveningPvKwh = EstimateWeatherPvKwh(dashboard.Weather, effectiveEveningStart, eveningEnd);
        var reservePct = projectedSocPct < 35 || eveningPvKwh < 0.25 ? 18d : 14d;
        var reserveKwh = batteryCapacityKwh * reservePct / 100d;
        var usableBatteryKwh = Math.Max(0, projectedBatteryKwh - reserveKwh);
        var expectedImportKwh = Math.Max(0, eveningConsumptionKwh - eveningPvKwh - usableBatteryKwh);
        var importPressurePct = eveningConsumptionKwh > 0.1 ? expectedImportKwh / eveningConsumptionKwh * 100 : 0;
        var lowSocPenalty = projectedSocPct < 25 ? 22 : projectedSocPct < 40 ? 12 : projectedSocPct < 55 ? 5 : 0;
        var weatherPenalty = (preEveningPvKwh + eveningPvKwh) < 0.8 ? 8 : 0;
        var currentImportPenalty = dashboard.LatestGridKw < -0.2 ? 6 : 0;
        var dataPenalty = dashboard.Weather.IsAvailable ? 0 : 8;
        var riskPct = Math.Clamp(
            importPressurePct * 0.72 + lowSocPenalty + weatherPenalty + currentImportPenalty + dataPenalty,
            0,
            100);
        var riskLevel = riskPct >= 72
            ? "danger"
            : riskPct >= 46
                ? "warning"
                : riskPct >= 24
                    ? "accent"
                    : "good";
        var summary = BuildEveningImportSummary(riskLevel, expectedImportKwh, projectedSocPct, eveningConsumptionKwh, eveningPvKwh);

        return new EveningImportPrediction
        {
            RiskLevel = riskLevel,
            RiskPct = Math.Round(riskPct, 0),
            WindowLabel = windowLabel,
            Summary = summary,
            ProjectedBatterySocPct = Math.Round(projectedSocPct, 0),
            EstimatedEveningConsumptionKwh = Math.Round(eveningConsumptionKwh, 2),
            PreEveningPvKwh = Math.Round(preEveningPvKwh, 2),
            EveningPvKwh = Math.Round(eveningPvKwh, 2),
            UsableBatteryKwh = Math.Round(usableBatteryKwh, 2),
            ExpectedImportKwh = Math.Round(expectedImportKwh, 2),
            BatteryReservePct = reservePct,
            Metrics =
            [
                new EveningRiskMetric(
                    "Spotreba vecer",
                    $"{eveningConsumptionKwh:0.0} kWh",
                    "model z telemetrie a dennej historie",
                    riskLevel is "danger" or "warning" ? "warning" : "accent"),
                new EveningRiskMetric(
                    "FV este do vecera",
                    $"{preEveningPvKwh:0.0} kWh",
                    "potencial pred 17:00 na dobitie baterie",
                    preEveningPvKwh >= 2 ? "good" : "warning"),
                new EveningRiskMetric(
                    "SOC na vecer",
                    $"{projectedSocPct:0} %",
                    $"pouzitelne {usableBatteryKwh:0.0} kWh nad rezervou {reservePct:0} %",
                    projectedSocPct >= 55 ? "good" : projectedSocPct >= 35 ? "accent" : "danger"),
                new EveningRiskMetric(
                    "Odhad importu",
                    $"{expectedImportKwh:0.0} kWh",
                    "kolko moze chybat po FV a baterii",
                    expectedImportKwh <= 0.2 ? "good" : expectedImportKwh <= 1.2 ? "warning" : "danger")
            ],
            Actions = BuildEveningImportActions(
                riskLevel,
                expectedImportKwh,
                projectedSocPct,
                preEveningPvKwh,
                eveningConsumptionKwh,
                dashboard.LatestWattKw)
        };
    }

    private static double EstimateConsumptionKwh(DashboardData dashboard, DateTime from, DateTime to, double profileMultiplier)
    {
        var hours = Math.Max(0, (to - from).TotalHours);
        if (hours <= 0)
        {
            return 0;
        }

        var samples = dashboard.Charts.Timeline
            .Select(point => new
            {
                LocalTime = DateTimeOffset.FromUnixTimeMilliseconds(point.Ts).LocalDateTime,
                point.ConsumptionKw
            })
            .Where(point =>
                point.ConsumptionKw > 0.03 &&
                IsTimeOfDayInside(point.LocalTime.TimeOfDay, from.TimeOfDay, to.TimeOfDay))
            .Select(point => point.ConsumptionKw)
            .ToArray();

        if (samples.Length >= 4)
        {
            return Math.Round(samples.Average() * hours, 2);
        }

        var recentDailyAverage = dashboard.History
            .TakeLast(14)
            .Select(day => day.Consumption)
            .Where(value => value > 0)
            .DefaultIfEmpty(dashboard.TodayConsumption > 0 ? dashboard.TodayConsumption : 0)
            .Average();
        var fallbackAverageKw = recentDailyAverage > 0
            ? recentDailyAverage / 24d * profileMultiplier
            : Math.Max(dashboard.BaseLoadKw, dashboard.LatestConsumptionKw * 0.75);

        return Math.Round(Math.Max(0.15, fallbackAverageKw) * hours, 2);
    }

    private static bool IsTimeOfDayInside(TimeSpan value, TimeSpan start, TimeSpan end)
    {
        return start <= end
            ? value >= start && value <= end
            : value >= start || value <= end;
    }

    private static double EstimateWeatherPvKwh(WeatherForecastSummary weather, DateTime from, DateTime to)
    {
        if (!weather.IsAvailable || weather.Hourly.Count == 0 || to <= from)
        {
            return 0;
        }

        return Math.Round(weather.Hourly
            .Where(point => DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time) &&
                time >= from &&
                time < to)
            .Sum(point => Math.Max(0, point.EstimatedPvKw)), 2);
    }

    private static WeatherForecastSummary ReconcileWeatherForecastWithTelemetry(DashboardData dashboard)
    {
        var weather = dashboard.Weather;
        if (!weather.IsAvailable || weather.Hourly.Count == 0 || dashboard.Charts.Timeline.Count == 0)
        {
            return weather;
        }

        var now = DateTime.Now;
        var today = now.Date;
        var actualPointsToday = dashboard.Charts.Timeline
            .Select(point => new
            {
                Time = DateTimeOffset.FromUnixTimeMilliseconds(point.Ts).LocalDateTime,
                PvKw = Math.Max(0, point.PvKw)
            })
            .Where(point => point.Time.Date == today && point.PvKw > 0.05)
            .ToArray();

        if (actualPointsToday.Length == 0)
        {
            return weather;
        }

        var weatherPointsToday = weather.Hourly
            .Select(point => new
            {
                Point = point,
                ParsedTime = DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                    ? parsed
                    : (DateTime?)null
            })
            .Where(entry => entry.ParsedTime.HasValue && entry.ParsedTime.Value.Date == today)
            .ToArray();

        if (weatherPointsToday.Length == 0)
        {
            return weather;
        }

        var actualPeakSoFar = actualPointsToday
            .Where(point => point.Time <= now.AddMinutes(10))
            .Select(point => point.PvKw)
            .DefaultIfEmpty(0)
            .Max();

        var forecastPeakSoFar = weatherPointsToday
            .Where(entry => entry.ParsedTime!.Value <= now.AddMinutes(30))
            .Select(entry => Math.Max(0, entry.Point.EstimatedPvKw))
            .DefaultIfEmpty(0)
            .Max();

        if (actualPeakSoFar <= 0.1 || forecastPeakSoFar <= 0.1)
        {
            return weather;
        }

        var maxUsefulForecastPeak = dashboard.InstalledPvKw > 0
            ? Math.Max(forecastPeakSoFar, dashboard.InstalledPvKw * 1.03)
            : forecastPeakSoFar * 1.6;
        var calibrationRatio = Math.Clamp(actualPeakSoFar / forecastPeakSoFar, 0.9, maxUsefulForecastPeak / forecastPeakSoFar);

        if (Math.Abs(calibrationRatio - 1) < 0.08)
        {
            return weather;
        }

        var adjustedHourly = weather.Hourly
            .Select(point =>
            {
                if (!DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var pointTime) ||
                    pointTime.Date != today)
                {
                    return point;
                }

                var adjustedPv = Math.Round(
                    Math.Clamp(point.EstimatedPvKw * calibrationRatio, 0, dashboard.InstalledPvKw > 0 ? dashboard.InstalledPvKw * 1.03 : point.EstimatedPvKw * calibrationRatio),
                    2);

                return new WeatherHourPoint(
                    point.Time,
                    point.Label,
                    point.TemperatureC,
                    point.CloudCoverPct,
                    point.PrecipitationMm,
                    point.WindKph,
                    point.ShortwaveRadiationWm2,
                    point.DirectRadiationWm2,
                    point.DiffuseRadiationWm2,
                    adjustedPv);
            })
            .ToArray();

        var adjustedToday = adjustedHourly
            .Where(point => DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var pointTime) &&
                pointTime.Date == today)
            .ToArray();

        var adjustedPeak = adjustedToday.Select(point => point.EstimatedPvKw).DefaultIfEmpty(0).Max();
        var adjustedDailyKwh = Math.Round(adjustedToday.Sum(point => Math.Max(0, point.EstimatedPvKw)), 1);
        var reconciledPeak = Math.Max(actualPeakSoFar, adjustedPeak);

        return new WeatherForecastSummary
        {
            SourceAddress = weather.SourceAddress,
            ResolvedLocation = weather.ResolvedLocation,
            Latitude = weather.Latitude,
            Longitude = weather.Longitude,
            CurrentTemperatureC = weather.CurrentTemperatureC,
            CurrentCloudCoverPct = weather.CurrentCloudCoverPct,
            CurrentWindKph = weather.CurrentWindKph,
            CurrentPrecipitationMm = weather.CurrentPrecipitationMm,
            EstimatedPvKwhToday = adjustedDailyKwh,
            PeakEstimatedPvKw = Math.Round(reconciledPeak, 2),
            Sunrise = weather.Sunrise,
            Sunset = weather.Sunset,
            Condition = weather.Condition,
            Summary = BuildWeatherSummaryFromTelemetry(adjustedDailyKwh, reconciledPeak, dashboard.InstalledPvKw, weather.CurrentCloudCoverPct, weather.CurrentPrecipitationMm),
            IsAvailable = weather.IsAvailable,
            Hourly = adjustedHourly
        };
    }

    private static string BuildWeatherSummaryFromTelemetry(double estimatedKwh, double peakPvKw, double installedPvKw, double cloudCoverPct, double precipitationMm)
    {
        var limitPct = installedPvKw > 0 ? peakPvKw / installedPvKw * 100 : 0;
        var weatherNote = precipitationMm > 0.2
            ? "Dazd moze znizit realnu vyrobu."
            : cloudCoverPct >= 70
                ? "Oblacnost bude hlavny limit."
                : "Model je priebezne kalibrovany podla dnesnej telemetrie.";

        return $"Odhad dnes {estimatedKwh:0.0} kWh, spicka asi {peakPvKw:0.0} kW ({limitPct:0} % instalacie). {weatherNote}";
    }

    private static string BuildEveningImportSummary(
        string riskLevel,
        double expectedImportKwh,
        double projectedSocPct,
        double eveningConsumptionKwh,
        double eveningPvKwh)
    {
        return riskLevel switch
        {
            "danger" => $"Vysoke riziko importu: vecer moze chybat okolo {expectedImportKwh:0.0} kWh. SOC sa odhaduje na {projectedSocPct:0} %, vecerna FV vyroba iba {eveningPvKwh:0.0} kWh.",
            "warning" => $"Import je mozny, ale da sa znizit riadenim spotreby. Odhad vecernej spotreby je {eveningConsumptionKwh:0.0} kWh a chyba asi {expectedImportKwh:0.0} kWh.",
            "accent" => $"Vecer vyzera takmer pokryty. Mala rezerva sa hodi, odhadovany import je len {expectedImportKwh:0.0} kWh.",
            _ => $"Vecer vyzera bezpecne bez vyznamneho importu. Bateria a zvysna FV vyroba by mali pokryt beznu spotrebu."
        };
    }

    private static IReadOnlyList<EveningRiskAction> BuildEveningImportActions(
        string riskLevel,
        double expectedImportKwh,
        double projectedSocPct,
        double preEveningPvKwh,
        double eveningConsumptionKwh,
        double latestWattKw)
    {
        var actions = new List<EveningRiskAction>();

        if (riskLevel is "danger" or "warning")
        {
            actions.Add(new EveningRiskAction(
                "Presun velke spotrebice pred vecer",
                $"Ak vies, posun bojler, pranie alebo nabijanie auta do casu s FV prebytkom. Potrebujeme stiahnut asi {Math.Min(expectedImportKwh, eveningConsumptionKwh):0.0} kWh.",
                "usetri import",
                "danger"));
            actions.Add(new EveningRiskAction(
                "Chran rezervu baterie",
                $"Ciel je mat okolo 17:00 aspon {Math.Max(45, projectedSocPct):0} % SOC alebo minimalne nevybijat bateriu zbytocne pred vecerom.",
                "SOC guard",
                "warning"));
        }
        else
        {
            actions.Add(new EveningRiskAction(
                "Vecer je pod kontrolou",
                "Predikcia nevidi velky deficit. System moze bezat normalne, len sleduj spicky medzi 18:00 a 21:00.",
                "OK",
                "good"));
        }

        if (preEveningPvKwh > 0.8)
        {
            actions.Add(new EveningRiskAction(
                "Vyuzit dnesne slnko pred 17:00",
                $"Pred vecerom je este odhad {preEveningPvKwh:0.0} kWh FV. Najlepsie je ho ulozit do baterie alebo zmysluplne spotrebovat doma.",
                "PV window",
                "accent"));
        }

        if (latestWattKw > 0.2)
        {
            actions.Add(new EveningRiskAction(
                "Skontroluj WattRouter",
                $"WattRouter teraz berie asi {latestWattKw:0.0} kW. Pri vyssom riziku ho vecer nepustaj do importu, nech nespotrebuje bateriovu rezervu.",
                "router",
                "warning"));
        }

        return actions.Take(4).ToArray();
    }

    private sealed record DashboardPreferenceState
    {
        public int Version { get; init; } = 1;
        public string? SystemSn { get; init; }
        public string? Day { get; init; }
        public int? HoursBack { get; init; }
        public string? TariffProviderKey { get; init; }
        public string? TariffPlanKey { get; init; }
        public string[] TrendSeries { get; init; } = [];
        public int? TrendWindowMin { get; init; }
        public int? TrendWindowMax { get; init; }
        public string? HistoryWindow { get; init; }
        public int? HistoryPage { get; init; }
        public string? WeatherDay { get; init; }
        public string? ActiveSection { get; init; }
        public string? UpdatedUtc { get; init; }
    }
}
