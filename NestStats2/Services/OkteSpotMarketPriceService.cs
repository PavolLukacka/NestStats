using System.Globalization;
using System.Text;
using NestStats2.Models;

namespace NestStats2.Services;

public sealed class OkteSpotMarketPriceService : ISpotMarketPriceService
{
    private static readonly CultureInfo CsvDateCulture = CultureInfo.GetCultureInfo("sk-SK");
    private static readonly object CacheLock = new();
    private static CacheEntry? _cache;

    private readonly HttpClient _httpClient;
    private readonly ILogger<OkteSpotMarketPriceService> _logger;

    public OkteSpotMarketPriceService(HttpClient httpClient, ILogger<OkteSpotMarketPriceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://isot.okte.sk/");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<SpotMarketSummary> GetSkDayAheadSummaryAsync(DateTime localToday, CancellationToken cancellationToken = default)
    {
        var day = localToday.Date;

        lock (CacheLock)
        {
            if (_cache is not null &&
                _cache.LocalToday == day &&
                DateTimeOffset.UtcNow - _cache.CreatedUtc < TimeSpan.FromMinutes(15))
            {
                return _cache.Value;
            }
        }

        try
        {
            var tomorrow = day.AddDays(1);
            var requestUri = $"api/v1/dam/report/detailed?lang=en-US&deliverydayfrom={day:yyyy-MM-dd}&deliverydayto={tomorrow:yyyy-MM-dd}&format=csv";
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var csv = Encoding.UTF8.GetString(bytes);
            var rows = ParseRows(csv).ToArray();

            var summary = new SpotMarketSummary
            {
                IsAvailable = rows.Length > 0,
                MarketArea = "SK",
                SourceLabel = "OKTE day-ahead market",
                SourceUrl = "https://okte.sk/en/short-term-market/published-information-of-dam/day-ahead-detailed-overview/",
                Disclaimer = "Spot ukazuje trhovu komoditnu cenu pre SK obchodnu oblast. Koncova cena pre odber v SR sa este sklada aj z distribucnych a dalsich regulovanych poloziek.",
                RetrievedAtLocal = localToday,
                Today = BuildDaySummary(day, "Dnes", rows, localToday),
                Tomorrow = BuildDaySummary(tomorrow, "Zajtra", rows, localToday)
            };

            lock (CacheLock)
            {
                _cache = new CacheEntry(DateTimeOffset.UtcNow, day, summary);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OKTE spot market prices for {Day}", day);
            return new SpotMarketSummary
            {
                IsAvailable = false,
                MarketArea = "SK",
                SourceLabel = "OKTE day-ahead market",
                SourceUrl = "https://okte.sk/en/short-term-market/published-information-of-dam/day-ahead-detailed-overview/",
                Disclaimer = "Spot ceny sa momentalne nepodarilo nacitat z OKTE.",
                RetrievedAtLocal = localToday,
                Today = BuildEmptyDaySummary(day, "Dnes"),
                Tomorrow = BuildEmptyDaySummary(day.AddDays(1), "Zajtra")
            };
        }
    }

    private static IEnumerable<RawSpotRow> ParseRows(string csv)
    {
        var lines = csv
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(';');
            if (columns.Length < 18)
            {
                continue;
            }

            if (!DateTime.TryParse(columns[0], CsvDateCulture, DateTimeStyles.AssumeLocal, out var deliveryDay))
            {
                continue;
            }

            if (!double.TryParse(columns[10], NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var price))
            {
                continue;
            }

            var intervalLabel = columns[2].Trim();
            var timePart = intervalLabel.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!TimeSpan.TryParseExact(timePart ?? string.Empty, @"hh\:mm", CultureInfo.InvariantCulture, out var startTime))
            {
                continue;
            }

            yield return new RawSpotRow(
                deliveryDay.Date.Add(startTime),
                intervalLabel,
                price,
                columns[17].Trim());
        }
    }

    private static SpotPriceDaySummary BuildDaySummary(
        DateTime date,
        string dayLabel,
        IReadOnlyList<RawSpotRow> rows,
        DateTime localNow)
    {
        var dayRows = rows
            .Where(row => row.StartLocal.Date == date.Date)
            .OrderBy(row => row.StartLocal)
            .ToArray();

        if (dayRows.Length == 0)
        {
            return BuildEmptyDaySummary(date, dayLabel);
        }

        var hourly = dayRows
            .GroupBy(row => row.StartLocal.Hour)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var start = date.Date.AddHours(group.Key);
                var price = Math.Round(group.Average(item => item.PriceEurPerMwh), 2);
                var isCurrent = localNow >= start && localNow < start.AddHours(1);
                return new SpotPriceHourPoint(
                    start,
                    start.ToString("HH:mm"),
                    $"{start:HH:mm} - {start.AddHours(1):HH:mm}",
                    price,
                    price < 0,
                    isCurrent);
            })
            .ToArray();

        var currentQuarter = dayRows
            .FirstOrDefault(row => localNow >= row.StartLocal && localNow < row.StartLocal.AddMinutes(15));

        var minRow = dayRows.OrderBy(row => row.PriceEurPerMwh).First();
        var maxRow = dayRows.OrderByDescending(row => row.PriceEurPerMwh).First();
        var latestStatus = dayRows
            .Select(row => row.PublicationStatus)
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .GroupBy(status => status)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "N/A";

        return new SpotPriceDaySummary
        {
            Date = date.Date,
            DayLabel = dayLabel,
            IsAvailable = true,
            PublicationStatus = latestStatus,
            AveragePriceEurPerMwh = Math.Round(dayRows.Average(row => row.PriceEurPerMwh), 2),
            MinPriceEurPerMwh = Math.Round(minRow.PriceEurPerMwh, 2),
            MinPriceLabel = minRow.IntervalLabel,
            MaxPriceEurPerMwh = Math.Round(maxRow.PriceEurPerMwh, 2),
            MaxPriceLabel = maxRow.IntervalLabel,
            CurrentIntervalPriceEurPerMwh = Math.Round(currentQuarter?.PriceEurPerMwh ?? hourly.First().PriceEurPerMwh, 2),
            CurrentIntervalLabel = currentQuarter?.IntervalLabel ?? hourly.First().IntervalLabel,
            HourlyPoints = hourly
        };
    }

    private static SpotPriceDaySummary BuildEmptyDaySummary(DateTime date, string dayLabel)
    {
        return new SpotPriceDaySummary
        {
            Date = date.Date,
            DayLabel = dayLabel,
            IsAvailable = false,
            PublicationStatus = "Nedostupne"
        };
    }

    private sealed record RawSpotRow(
        DateTime StartLocal,
        string IntervalLabel,
        double PriceEurPerMwh,
        string PublicationStatus);

    private sealed record CacheEntry(DateTimeOffset CreatedUtc, DateTime LocalToday, SpotMarketSummary Value);
}
