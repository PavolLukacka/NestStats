namespace NestStats2.Models;

public sealed class SystemInfo
{
    public string sn_number { get; set; } = string.Empty;
    public string system_name { get; set; } = string.Empty;
    public string? system_address { get; set; }
    public int? pocet_ssr { get; set; }
    public string? popis { get; set; }
}

public sealed class WattRouterInfo
{
    public DateTimeOffset created_at { get; set; }
    public string sn_number { get; set; } = string.Empty;
    public double? powerPercentage { get; set; }
    public bool? relay2On { get; set; }
    public bool? relay3On { get; set; }
    public bool? relay4On { get; set; }
    public bool? relay5On { get; set; }
    public bool? relay6On { get; set; }
    public bool? relay7On { get; set; }
    public bool? relay8On { get; set; }
    public bool? gridFetch { get; set; }

    public int RelaysOnCount =>
        (relay2On == true ? 1 : 0) +
        (relay3On == true ? 1 : 0) +
        (relay4On == true ? 1 : 0) +
        (relay5On == true ? 1 : 0) +
        (relay6On == true ? 1 : 0) +
        (relay7On == true ? 1 : 0) +
        (relay8On == true ? 1 : 0);

    public int ActiveChannelCount => RelaysOnCount + ((powerPercentage ?? 0) > 0.1 ? 1 : 0);

    public double GetAverageChannelLoadPercentage(int relayChannelCount)
    {
        var normalizedRelayCount = NormalizeRelayChannelCount(relayChannelCount);
        return Math.Round((((powerPercentage ?? 0) + (RelaysOnCount * 100d)) / normalizedRelayCount), 1);
    }

    public IReadOnlyList<RelayState> ToRelayStates(double relayCapacityKw, int relayChannelCount)
    {
        var normalizedRelayCount = NormalizeRelayChannelCount(relayChannelCount);
        var states = new List<RelayState>
        {
            new RelayState(
                "SSR 1",
                (powerPercentage ?? 0) > 0.1,
                "Plynula regulacia vykonu",
                Math.Round(powerPercentage ?? 0, 1),
                Math.Round(((powerPercentage ?? 0) / 100d) * relayCapacityKw, 2),
                "variable"),
            BuildBinaryRelay("SSR 2", relay2On, "Sekcia 2", relayCapacityKw),
            BuildBinaryRelay("SSR 3", relay3On, "Sekcia 3", relayCapacityKw),
            BuildBinaryRelay("SSR 4", relay4On, "Sekcia 4", relayCapacityKw),
            BuildBinaryRelay("SSR 5", relay5On, "Sekcia 5", relayCapacityKw),
            BuildBinaryRelay("SSR 6", relay6On, "Sekcia 6", relayCapacityKw),
            BuildBinaryRelay("SSR 7", relay7On, "Sekcia 7", relayCapacityKw),
            BuildBinaryRelay("SSR 8", relay8On, "Sekcia 8", relayCapacityKw)
        };

        for (var relayIndex = states.Count + 1; relayIndex <= normalizedRelayCount; relayIndex++)
        {
            states.Add(new RelayState(
                $"SSR {relayIndex}",
                false,
                "Konfigurovany kanal",
                0,
                0,
                "binary"));
        }

        return states.Take(normalizedRelayCount).ToArray();
    }

    private static RelayState BuildBinaryRelay(string name, bool? state, string detail, double relayCapacityKw)
    {
        var isOn = state == true;
        return new RelayState(
            name,
            isOn,
            detail,
            isOn ? 100 : 0,
            isOn ? relayCapacityKw : 0,
            "binary");
    }

    private static int NormalizeRelayChannelCount(int relayChannelCount)
    {
        if (relayChannelCount <= 0)
        {
            relayChannelCount = 8;
        }

        return Math.Clamp(relayChannelCount, 1, 9);
    }
}

public sealed class GridInformation
{
    public DateTimeOffset created_at { get; set; }
    public string sn_number { get; set; } = string.Empty;
    public double? grid_frequency { get; set; }
    public double? active_power_output_total { get; set; }
    public double? active_power_pcc_total { get; set; }
    public double? voltage_phase_r { get; set; }
    public double? current_output_r { get; set; }
    public double? active_power_output_r { get; set; }
    public double? current_pcc_r { get; set; }
    public double? active_power_pcc_r { get; set; }
    public double? voltage_phase_s { get; set; }
    public double? current_output_s { get; set; }
    public double? active_power_output_s { get; set; }
    public double? current_pcc_s { get; set; }
    public double? active_power_pcc_s { get; set; }
    public double? voltage_phase_t { get; set; }
    public double? current_output_t { get; set; }
    public double? active_power_output_t { get; set; }
    public double? current_pcc_t { get; set; }
    public double? active_power_pcc_t { get; set; }
}

public sealed class PvInformation
{
    public DateTimeOffset created_at { get; set; }
    public string sn_number { get; set; } = string.Empty;
    public short? mppt { get; set; }
    public float? voltage { get; set; }
    public float? current { get; set; }
    public float? power { get; set; }
}

public sealed class BatteryInformation
{
    public DateTimeOffset created_at { get; set; }
    public string sn_number { get; set; } = string.Empty;
    public float? voltage { get; set; }
    public double? current { get; set; }
    public float? power { get; set; }
    public float? temperature { get; set; }
    public int? soc { get; set; }
    public int? soh { get; set; }
    public int? charge_cycle { get; set; }
}

public sealed class StatisticalInformation
{
    public double? pv_generation_today { get; set; }
    public double? pv_generation_total { get; set; }
    public double? consumption_today { get; set; }
    public double? consumption_total { get; set; }
    public double? purchase_today { get; set; }
    public double? purchase_total { get; set; }
    public double? sell_today { get; set; }
    public double? sell_total { get; set; }
    public double? battery_charge_today { get; set; }
    public double? battery_charge_total { get; set; }
    public double? battery_discharge_today { get; set; }
    public double? battery_discharge_total { get; set; }
    public DateTimeOffset timestamp { get; set; }
    public string sn_number { get; set; } = string.Empty;
}

public sealed class LiveSnapshot
{
    public DateTimeOffset time { get; set; }
    public double? wattPowerPercentage { get; set; }
    public double? wattPowerKw { get; set; }
    public int? relayCount { get; set; }
    public double? relayAverageLoadPct { get; set; }
    public double? wattUtilizationPct { get; set; }
    public double? gridPower { get; set; }
    public double? inverterPower { get; set; }
    public double? gridFrequency { get; set; }
    public double? pvPower { get; set; }
    public double? pvTotal { get; set; }
    public double? pvSaturationPct { get; set; }
    public double? pvVoltage { get; set; }
    public double? pvCurrent { get; set; }
    public double? mppt1Power { get; set; }
    public double? mppt2Power { get; set; }
    public double? mppt1Voltage { get; set; }
    public double? mppt2Voltage { get; set; }
    public double? mppt1Current { get; set; }
    public double? mppt2Current { get; set; }
    public double? batteryPower { get; set; }
    public double? batteryVoltage { get; set; }
    public double? batteryCurrent { get; set; }
    public double? batteryTemperature { get; set; }
    public double? consumption { get; set; }
    public int? soc { get; set; }
    public int? soh { get; set; }
    public int? chargeCycle { get; set; }
    public bool? gridFetch { get; set; }
    public IReadOnlyList<RelayState> relayStates { get; set; } = [];
}

public sealed record RelayState(
    string Name,
    bool IsOn,
    string Detail,
    double LoadPercentage,
    double PowerKw,
    string Mode);

public sealed record InsightItem(string Title, string Description, string Tone);

public sealed record StatusPill(string Label, string Value, string Tone);

public sealed record DeviceState(string Name, string Value, string Detail, string Tone);

public sealed record ForecastMetric(string Label, string Value, string Detail, string Tone);

public sealed record SystemAnomaly(string Title, string Detail, string Severity, string Metric, string Value);

public sealed record OperatorRecommendation(string Title, string Detail, string Impact, string Tone);

public sealed record EveningRiskMetric(string Label, string Value, string Detail, string Tone);

public sealed record EveningRiskAction(string Title, string Detail, string Impact, string Tone);

public sealed record DashboardLoadProgressUpdate(int Percent, string Stage, string Detail);

public sealed class SmartForecastSummary
{
    public double TomorrowPvKwh { get; init; }
    public double TomorrowConsumptionKwh { get; init; }
    public double TomorrowImportKwh { get; init; }
    public double TomorrowExportKwh { get; init; }
    public double ConfidencePct { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<ForecastMetric> Metrics { get; init; } = [];
}

public sealed record WeatherHourPoint(
    string Time,
    string Label,
    double TemperatureC,
    double CloudCoverPct,
    double PrecipitationMm,
    double WindKph,
    double ShortwaveRadiationWm2,
    double DirectRadiationWm2,
    double DiffuseRadiationWm2,
    double EstimatedPvKw);

public sealed class WeatherForecastSummary
{
    public string SourceAddress { get; init; } = string.Empty;
    public string ResolvedLocation { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double CurrentTemperatureC { get; init; }
    public double CurrentCloudCoverPct { get; init; }
    public double CurrentWindKph { get; init; }
    public double CurrentPrecipitationMm { get; init; }
    public double EstimatedPvKwhToday { get; init; }
    public double PeakEstimatedPvKw { get; init; }
    public string Sunrise { get; init; } = string.Empty;
    public string Sunset { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public IReadOnlyList<WeatherHourPoint> Hourly { get; init; } = [];
}

public sealed record SpotPriceHourPoint(
    DateTime StartLocal,
    string Label,
    string IntervalLabel,
    double PriceEurPerMwh,
    bool IsNegative,
    bool IsCurrentInterval);

public sealed class SpotPriceDaySummary
{
    public DateTime Date { get; init; }
    public string DayLabel { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public string PublicationStatus { get; init; } = string.Empty;
    public double AveragePriceEurPerMwh { get; init; }
    public double MinPriceEurPerMwh { get; init; }
    public string MinPriceLabel { get; init; } = string.Empty;
    public double MaxPriceEurPerMwh { get; init; }
    public string MaxPriceLabel { get; init; } = string.Empty;
    public double CurrentIntervalPriceEurPerMwh { get; init; }
    public string CurrentIntervalLabel { get; init; } = string.Empty;
    public IReadOnlyList<SpotPriceHourPoint> HourlyPoints { get; init; } = [];
}

public sealed class SpotMarketSummary
{
    public bool IsAvailable { get; init; }
    public string MarketArea { get; init; } = "SK";
    public string SourceLabel { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string Disclaimer { get; init; } = string.Empty;
    public DateTime RetrievedAtLocal { get; init; }
    public SpotPriceDaySummary Today { get; init; } = new();
    public SpotPriceDaySummary Tomorrow { get; init; } = new();
}

public sealed class DailyEnergyStory
{
    public string Key { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string DayLabel { get; init; } = string.Empty;
    public string Headline { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Tone { get; init; } = "neutral";
    public string BalanceLabel { get; init; } = string.Empty;
    public string AutonomyLabel { get; init; } = string.Empty;
    public string SelfUseLabel { get; init; } = string.Empty;
    public double PvKwh { get; init; }
    public double ConsumptionKwh { get; init; }
    public double ImportKwh { get; init; }
    public double ExportKwh { get; init; }
    public double BalanceKwh { get; init; }
    public double SelfSufficiencyPct { get; init; }
    public double SelfUsePct { get; init; }
    public double? PreviousPvDeltaKwh { get; init; }
    public IReadOnlyList<string> Highlights { get; init; } = [];
}

public sealed class EveningImportPrediction
{
    public string RiskLevel { get; init; } = "good";
    public double RiskPct { get; init; }
    public string WindowLabel { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public double ProjectedBatterySocPct { get; init; }
    public double EstimatedEveningConsumptionKwh { get; init; }
    public double PreEveningPvKwh { get; init; }
    public double EveningPvKwh { get; init; }
    public double UsableBatteryKwh { get; init; }
    public double ExpectedImportKwh { get; init; }
    public double BatteryReservePct { get; init; }
    public IReadOnlyList<EveningRiskMetric> Metrics { get; init; } = [];
    public IReadOnlyList<EveningRiskAction> Actions { get; init; } = [];
}

public sealed record TimeSeriesPoint(
    long Ts,
    string Label,
    double PvKw,
    double ConsumptionKw,
    double GridKw,
    double BatteryKw,
    double InverterKw,
    double WattKw,
    double SoC,
    double PvSaturationPct,
    double WattUtilizationPct,
    double RelayAverageLoadPct,
    int RelaysOn,
    double Mppt1Kw,
    double Mppt2Kw,
    bool GridFetch,
    double PvVoltageV,
    double PvCurrentA,
    double Mppt1VoltageV,
    double Mppt2VoltageV,
    double Mppt1CurrentA,
    double Mppt2CurrentA,
    double BatteryVoltageV,
    double BatteryCurrentA,
    double BatteryTemperatureC,
    double BatterySoH,
    double BatteryChargeCycle,
    double VoltagePhaseR,
    double VoltagePhaseS,
    double VoltagePhaseT,
    double CurrentOutputR,
    double CurrentOutputS,
    double CurrentOutputT,
    double ActivePowerOutputR,
    double ActivePowerOutputS,
    double ActivePowerOutputT,
    double CurrentPccR,
    double CurrentPccS,
    double CurrentPccT,
    double ActivePowerPccR,
    double ActivePowerPccS,
    double ActivePowerPccT,
    double GridFrequencyHz);

public sealed record DailyHistoryPoint(
    string Date,
    string Label,
    double Pv,
    double Consumption,
    double Import,
    double Export,
    double BatteryCharge,
    double BatteryDischarge,
    double DirectUseKwh,
    double SelfUseKwh,
    double SelfUsePct,
    double SelfSufficiencyKwh,
    double SelfSufficiencyPct);

public sealed record DailyTotalPoint(
    string Date,
    string Label,
    double PvTotal,
    double ConsumptionTotal,
    double ImportTotal,
    double ExportTotal,
    double BatteryChargeTotal,
    double BatteryDischargeTotal);

public sealed class DashboardChartPayload
{
    public IReadOnlyList<TimeSeriesPoint> Timeline { get; init; } = [];
    public IReadOnlyList<DailyHistoryPoint> History { get; init; } = [];
    public IReadOnlyList<DailyTotalPoint> Totals { get; init; } = [];
}

public sealed class DashboardBootstrap
{
    public string LiveEndpoint { get; init; } = string.Empty;
    public string HistoryEndpoint { get; init; } = string.Empty;
    public string LoadStartEndpoint { get; init; } = string.Empty;
    public string LoadProgressEndpoint { get; init; } = string.Empty;
    public int RefreshSeconds { get; init; }
    public double WattMaxKw { get; init; }
    public double InstalledPvKw { get; init; }
    public double BatteryCapacityKwh { get; init; }
    public int RelayChannelCount { get; init; }
    public string DefaultProviderKey { get; init; } = string.Empty;
    public string DefaultTariffKey { get; init; } = string.Empty;
    public DashboardChartPayload Charts { get; init; } = new();
    public LiveSnapshot? InitialLive { get; init; }
    public IReadOnlyList<TariffBenchmarkResult> TariffBenchmarks { get; init; } = [];
    public IReadOnlyList<EnergyBreakdownBar> EnergyBreakdowns { get; init; } = [];
    public SmartForecastSummary SmartForecast { get; init; } = new();
    public IReadOnlyList<SystemAnomaly> Anomalies { get; init; } = [];
    public IReadOnlyList<OperatorRecommendation> OperatorRecommendations { get; init; } = [];
    public WeatherForecastSummary Weather { get; set; } = new();
    public EveningImportPrediction EveningImport { get; set; } = new();
    public ExportRevenueSummary ExportRevenue { get; init; } = new();
    public EnvironmentalBenefitSummary EnvironmentalBenefits { get; init; } = new();
    public SpotMarketSummary SpotMarket { get; init; } = new();
    public IReadOnlyList<DailyEnergyStory> DailyStories { get; init; } = [];
}

public sealed class DashboardData
{
    public IReadOnlyList<SystemInfo> Systems { get; init; } = [];
    public string SelectedSnNumber { get; init; } = string.Empty;
    public string SystemName { get; init; } = "NestStats";
    public string SystemAddress { get; init; } = "Lokalita nie je zadana";

    public DateTimeOffset RangeStartUtc { get; init; }
    public DateTimeOffset RangeEndUtc { get; init; }
    public DateTime RangeStartLocal { get; init; }
    public DateTime RangeEndLocal { get; init; }
    public DateTime? SelectedDay { get; init; }
    public int HoursBack { get; init; }

    public SystemProfile SystemProfile { get; init; } = new();
    public double InstalledPvKw { get; init; }
    public double BatteryCapacityKwh { get; init; }
    public double WattMaxKw { get; init; }
    public int RelayChannelCount { get; init; } = 8;

    public IReadOnlyList<WattRouterInfo> WattPoints { get; init; } = [];
    public IReadOnlyList<double> WattPowerKwSeries { get; init; } = [];
    public IReadOnlyList<PvInformation> PvPoints { get; init; } = [];
    public IReadOnlyList<BatteryInformation> BatteryPoints { get; init; } = [];
    public IReadOnlyList<GridInformation> GridPoints { get; init; } = [];
    public IReadOnlyList<DailyHistoryPoint> History { get; init; } = [];

    public WattRouterInfo? LatestWatt { get; init; }
    public GridInformation? LatestGrid { get; init; }
    public BatteryInformation? LatestBattery { get; init; }
    public StatisticalInformation? LatestStats { get; init; }
    public LiveSnapshot? InitialLive { get; init; }

    public double LatestPvKw { get; init; }
    public double LatestConsumptionKw { get; init; }
    public double LatestGridKw { get; init; }
    public double LatestBatteryKw { get; init; }
    public double LatestInverterKw { get; init; }
    public double LatestWattKw { get; init; }
    public double LatestBatterySoC { get; init; }
    public double LatestBatteryTempC { get; init; }
    public double CurrentPvVoltageV { get; init; }
    public double CurrentPvCurrentA { get; init; }
    public double CurrentBatteryVoltageV { get; init; }
    public double CurrentBatteryCurrentA { get; init; }
    public double CurrentGridFrequencyHz { get; init; }
    public double CurrentMpptImbalancePct { get; init; }
    public double CurrentPvSaturationPct { get; init; }
    public double CurrentWattUtilizationPct { get; init; }
    public double CurrentRelayAverageLoadPct { get; init; }

    public double TodayPv { get; init; }
    public double TodayConsumption { get; init; }
    public double TodayImport { get; init; }
    public double TodayExport { get; init; }
    public double TodayBatteryCharge { get; init; }
    public double TodayBatteryDischarge { get; init; }
    public double TodaySelfUseKwh { get; init; }
    public double SelfConsumptionPct { get; init; }
    public double SelfSufficiencyPct { get; init; }
    public double GridDependencyPct { get; init; }
    public double ExportLossPct { get; init; }
    public double FullLoadHoursToday { get; init; }
    public double SpecificYieldToday { get; init; }

    public double AvgWattKw { get; init; }
    public double MaxWattKw { get; init; }
    public double RelayOnPercentage { get; init; }
    public double TotalWattEnergyKwh { get; init; }
    public double AveragePvKw { get; init; }
    public double PeakPvKw { get; init; }
    public double PeakConsumptionKw { get; init; }
    public double BaseLoadKw { get; init; }
    public double PeakImportKw { get; init; }
    public double PeakExportKw { get; init; }
    public double PvSaturationPct { get; init; }
    public double BatteryAutonomyHours { get; init; }
    public double BatteryThroughputTodayKwh { get; init; }
    public double BatteryCycleToday { get; init; }
    public double MinBatterySoc { get; init; }
    public double AverageBatterySoc { get; init; }
    public double MaxBatterySoc { get; init; }
    public double BatteryRoundtripProxyPct { get; init; }
    public double DailyWattCapturePct { get; init; }
    public double ImportWindowPct { get; init; }
    public double ExportWindowPct { get; init; }
    public double AverageBatteryTempC { get; init; }
    public double PeakBatteryTempC { get; init; }
    public double AverageGridFrequencyHz { get; init; }
    public double EnergyScore { get; init; }
    public double DataFreshnessMinutes { get; init; }

    public double LifetimePv { get; init; }
    public double LifetimeConsumption { get; init; }
    public double LifetimeImport { get; init; }
    public double LifetimeExport { get; init; }
    public double LifetimeBatteryCharge { get; init; }
    public double LifetimeBatteryDischarge { get; init; }
    public double LifetimeSelfUseKwh { get; init; }
    public double LifetimeSelfSufficiencyKwh { get; init; }
    public double ProjectedMonthlyPvKwh { get; init; }
    public double ProjectedAnnualPvKwh { get; init; }
    public double ProjectedMonthlySelfSufficiencyKwh { get; init; }
    public double ProjectedAnnualSelfSufficiencyKwh { get; init; }
    public double ProjectedMonthlyExportKwh { get; init; }
    public double ProjectedAnnualExportKwh { get; init; }

    public IReadOnlyList<RelayState> RelayStates { get; init; } = [];
    public IReadOnlyList<StatusPill> StatusPills { get; init; } = [];
    public IReadOnlyList<DeviceState> DeviceStates { get; init; } = [];
    public IReadOnlyList<InsightItem> Insights { get; init; } = [];
    public IReadOnlyList<TariffBenchmarkResult> TariffBenchmarks { get; init; } = [];
    public IReadOnlyList<EnergyBreakdownBar> EnergyBreakdowns { get; init; } = [];
    public SmartForecastSummary SmartForecast { get; init; } = new();
    public IReadOnlyList<SystemAnomaly> Anomalies { get; init; } = [];
    public IReadOnlyList<OperatorRecommendation> OperatorRecommendations { get; init; } = [];
    public WeatherForecastSummary Weather { get; set; } = new();
    public EveningImportPrediction EveningImport { get; set; } = new();
    public ExportRevenueSummary ExportRevenue { get; init; } = new();
    public EnvironmentalBenefitSummary EnvironmentalBenefits { get; init; } = new();
    public DashboardChartPayload Charts { get; init; } = new();
    public SpotMarketSummary SpotMarket { get; init; } = new();
    public IReadOnlyList<DailyEnergyStory> DailyStories { get; init; } = [];
}
