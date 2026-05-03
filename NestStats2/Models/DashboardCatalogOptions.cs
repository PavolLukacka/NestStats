namespace NestStats2.Models;

public sealed class DashboardCatalogOptions
{
    public const string SectionName = "DashboardCatalog";

    public SystemProfile DefaultSystemProfile { get; set; } = new();

    public Dictionary<string, SystemProfile> SystemProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TariffBenchmark> TariffBenchmarks { get; set; } = [];

    public ExportBenchmark ExportBenchmark { get; set; } = new();

    public EnvironmentalFactors EnvironmentalFactors { get; set; } = new();
}

public sealed class SystemProfile
{
    public string Label { get; set; } = "Residential FVE";

    public double InstalledPvKw { get; set; } = 10.0;

    public double BatteryCapacityKwh { get; set; } = 10.24;

    public double WattMaxKw { get; set; } = 16.0;
}

public sealed class TariffBenchmark
{
    public string Key { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string DistributorName { get; set; } = string.Empty;

    public string TariffCode { get; set; } = string.Empty;

    public string TariffLabel { get; set; } = string.Empty;

    public string ProductType { get; set; } = "fixed";

    public double HighRateEurPerKwh { get; set; }

    public double? LowRateEurPerKwh { get; set; }

    public double MonthlyFixedFeeEur { get; set; }

    public double LowTariffSharePct { get; set; }

    public bool IncludesDistribution { get; set; }

    public bool IncludesVat { get; set; }

    public string EffectiveDate { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string AssumptionLabel { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;
}

public sealed class ExportBenchmark
{
    public string Label { get; set; } = "ZSE prebytky";

    public double RateEurPerKwh { get; set; } = 0.009;

    public string EffectiveDate { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;
}

public sealed class EnvironmentalFactors
{
    public double Co2KgPerKwh { get; set; } = 0.23;

    public double CoalKgPerKwh { get; set; } = 0.28;

    public double TreeKgCo2PerYear { get; set; } = 21.0;
}

public sealed class TariffBenchmarkResult
{
    public string Key { get; init; } = string.Empty;

    public string ProviderKey { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public string DistributorName { get; init; } = string.Empty;

    public string TariffCode { get; init; } = string.Empty;

    public string TariffLabel { get; init; } = string.Empty;

    public string ProductType { get; init; } = "fixed";

    public string EffectiveDate { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string AssumptionLabel { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public double HighRateEurPerKwh { get; init; }

    public double? LowRateEurPerKwh { get; init; }

    public double EffectiveImportRateEurPerKwh { get; init; }

    public double MonthlyFixedFeeEur { get; init; }

    public double AnnualFixedFeeEur { get; init; }

    public double EstimatedLowTariffSharePct { get; init; }

    public bool IncludesDistribution { get; init; }

    public bool IncludesVat { get; init; }

    public double DailyAvoidedCostEur { get; init; }

    public double MonthlyAvoidedCostEur { get; init; }

    public double AnnualAvoidedCostEur { get; init; }

    public double LifetimeAvoidedCostEur { get; init; }

    public double NetDailyBenefitEur { get; init; }

    public double NetMonthlyBenefitEur { get; init; }

    public double NetAnnualBenefitEur { get; init; }
}

public sealed record EnergyBarSegment(
    string Label,
    double ValueKwh,
    string Tone);

public sealed class EnergyBreakdownBar
{
    public string Title { get; init; } = string.Empty;

    public double TotalKwh { get; init; }

    public IReadOnlyList<EnergyBarSegment> Segments { get; init; } = [];
}

public sealed class ExportRevenueSummary
{
    public string Label { get; init; } = string.Empty;

    public string EffectiveDate { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public double RateEurPerKwh { get; init; }

    public double DailyRevenueEur { get; init; }

    public double MonthlyRevenueEur { get; init; }

    public double AnnualRevenueEur { get; init; }

    public double LifetimeRevenueEur { get; init; }
}

public sealed class EnvironmentalBenefitSummary
{
    public double TodayCo2SavedKg { get; init; }

    public double MonthlyCo2SavedKg { get; init; }

    public double AnnualCo2SavedTons { get; init; }

    public double Co2SavedTons { get; init; }

    public double TodayCoalSavedKg { get; init; }

    public double AnnualCoalSavedTons { get; init; }

    public double CoalSavedTons { get; init; }

    public double TreesEquivalent { get; init; }

    public double AnnualTreesEquivalent { get; init; }

    public double TodayYieldEur { get; init; }

    public double MonthlyYieldEur { get; init; }

    public double AnnualYieldEur { get; init; }

    public double LifetimeYieldEur { get; init; }
}
