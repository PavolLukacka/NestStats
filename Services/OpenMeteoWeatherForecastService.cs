using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NestStats2.Models;

namespace NestStats2.Services;

public sealed class OpenMeteoWeatherForecastService : IWeatherForecastService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenMeteoWeatherForecastService> _logger;

    public OpenMeteoWeatherForecastService(HttpClient httpClient, ILogger<OpenMeteoWeatherForecastService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NestStats2/1.0 energy-dashboard");
    }

    public async Task<WeatherForecastSummary> GetForecastAsync(
        string? address,
        double installedPvKw,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return new WeatherForecastSummary
            {
                Condition = "Bez adresy",
                Summary = "Pre pocasie dopln adresu systemu v databaze."
            };
        }

        var cacheKey = $"{address.Trim()}|{installedPvKw:0.###}|{DateTime.Now:yyyy-MM-dd-HH}";
        if (Cache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.CreatedUtc < CacheDuration)
        {
            return cached.Value;
        }

        try
        {
            var location = await GeocodeAsync(address.Trim(), cancellationToken);
            if (location is null)
            {
                return new WeatherForecastSummary
                {
                    SourceAddress = address,
                    Condition = "Lokalita nenajdena",
                    Summary = "Adresu sa nepodarilo sparovat s polohou pre predpoved."
                };
            }

            var forecast = await FetchForecastAsync(location, installedPvKw, address.Trim(), cancellationToken);
            Cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow, forecast);
            return forecast;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Weather forecast failed for {Address}", address);
            return new WeatherForecastSummary
            {
                SourceAddress = address,
                Condition = "Pocasie nedostupne",
                Summary = "Externu predpoved sa nepodarilo nacitat. Dashboard pokracuje bez nej."
            };
        }
    }

    private async Task<GeoLocation?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" +
                  Uri.EscapeDataString(address);

        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<NominatimResult>>(stream, JsonOptions, cancellationToken);
        var row = rows?.FirstOrDefault();
        if (row is null ||
            !double.TryParse(row.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(row.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeoLocation(lat, lon, row.DisplayName ?? address);
    }

    private async Task<WeatherForecastSummary> FetchForecastAsync(
        GeoLocation location,
        double installedPvKw,
        string sourceAddress,
        CancellationToken cancellationToken)
    {
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                  "&hourly=temperature_2m,cloud_cover,precipitation,wind_speed_10m,shortwave_radiation,direct_radiation,diffuse_radiation" +
                  "&daily=sunrise,sunset" +
                  "&forecast_days=7&past_days=7&timezone=auto";

        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOptions, cancellationToken);
        var hourly = payload?.Hourly;
        if (hourly?.Time is null || hourly.Time.Length == 0)
        {
            return new WeatherForecastSummary
            {
                SourceAddress = sourceAddress,
                ResolvedLocation = location.DisplayName,
                Latitude = Math.Round(location.Latitude, 5),
                Longitude = Math.Round(location.Longitude, 5),
                Condition = "Bez hodinovej predpovede",
                Summary = "Poloha bola najdena, ale predpoved neobsahuje hodinove data."
            };
        }

        var now = DateTime.Now;
        var today = now.Date;
        var points = new List<WeatherHourPoint>();
        for (var i = 0; i < hourly.Time.Length; i++)
        {
            if (!DateTime.TryParse(hourly.Time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time))
            {
                continue;
            }

            var radiation = At(hourly.ShortwaveRadiation, i);
            var directRadiation = At(hourly.DirectRadiation, i);
            var diffuseRadiation = At(hourly.DiffuseRadiation, i);
            var cloud = At(hourly.CloudCover, i);
            var precipitation = At(hourly.Precipitation, i);
            var temperature = At(hourly.Temperature, i);
            var estimatedPv = EstimatePvKw(installedPvKw, radiation, directRadiation, diffuseRadiation, cloud, precipitation, temperature);

            points.Add(new WeatherHourPoint(
                time.ToString("yyyy-MM-ddTHH:mm:ss"),
                time.ToString("HH:mm"),
                Math.Round(temperature, 1),
                Math.Round(cloud, 0),
                Math.Round(precipitation, 2),
                Math.Round(At(hourly.WindSpeed, i), 1),
                Math.Round(radiation, 0),
                Math.Round(directRadiation, 0),
                Math.Round(diffuseRadiation, 0),
                estimatedPv));
        }

        var current = points
            .OrderBy(point => Math.Abs((DateTime.Parse(point.Time, CultureInfo.InvariantCulture) - now).TotalMinutes))
            .FirstOrDefault();

        var todayPoints = points
            .Where(point => DateTime.Parse(point.Time, CultureInfo.InvariantCulture).Date == today)
            .ToArray();
        var estimatedKwh = EstimateDailyKwh(todayPoints);
        var peakPv = todayPoints.Select(x => x.EstimatedPvKw).DefaultIfEmpty().Max();
        var condition = DescribeCondition(current?.CloudCoverPct ?? 0, current?.PrecipitationMm ?? 0, current?.ShortwaveRadiationWm2 ?? 0);
        var summary = BuildSummary(estimatedKwh, peakPv, installedPvKw, current?.CloudCoverPct ?? 0, current?.PrecipitationMm ?? 0);

        return new WeatherForecastSummary
        {
            SourceAddress = sourceAddress,
            ResolvedLocation = location.DisplayName,
            Latitude = Math.Round(location.Latitude, 5),
            Longitude = Math.Round(location.Longitude, 5),
            CurrentTemperatureC = current?.TemperatureC ?? 0,
            CurrentCloudCoverPct = current?.CloudCoverPct ?? 0,
            CurrentWindKph = current?.WindKph ?? 0,
            CurrentPrecipitationMm = current?.PrecipitationMm ?? 0,
            EstimatedPvKwhToday = estimatedKwh,
            PeakEstimatedPvKw = Math.Round(peakPv, 2),
            Sunrise = payload?.Daily?.Sunrise?.FirstOrDefault() is { } sunrise ? FormatTime(sunrise) : string.Empty,
            Sunset = payload?.Daily?.Sunset?.FirstOrDefault() is { } sunset ? FormatTime(sunset) : string.Empty,
            Condition = condition,
            Summary = summary,
            IsAvailable = true,
            Hourly = points
        };
    }

    private static double At(double[]? values, int index)
    {
        return values is not null && index >= 0 && index < values.Length ? values[index] : 0;
    }

    private static double EstimatePvKw(
        double installedPvKw,
        double shortwaveRadiationWm2,
        double directRadiationWm2,
        double diffuseRadiationWm2,
        double cloudCoverPct,
        double precipitationMm,
        double ambientTemperatureC)
    {
        if (installedPvKw <= 0 || shortwaveRadiationWm2 <= 0)
        {
            return 0;
        }

        var transposedIrradiance = Math.Max(
            shortwaveRadiationWm2 * 1.06,
            (directRadiationWm2 * 1.04) + (diffuseRadiationWm2 * 0.92));
        var directShare = (directRadiationWm2 + diffuseRadiationWm2) > 0
            ? directRadiationWm2 / (directRadiationWm2 + diffuseRadiationWm2)
            : 0;
        var orientationGain = Math.Clamp(0.985 + directShare * 0.085, 0.98, 1.07);
        var inverterDerate = 0.965;
        var soilingDerate = 0.99;
        var cableDerate = 0.995;
        var rainDerate = precipitationMm > 0.1 ? Math.Clamp(1 - precipitationMm * 0.04, 0.82, 1) : 1;
        var cloudFineTune = shortwaveRadiationWm2 > 450
            ? Math.Clamp(1.02 - (cloudCoverPct / 100d * 0.025), 0.985, 1.02)
            : 1;
        var cellTemperatureC = ambientTemperatureC + (transposedIrradiance * 0.028);
        var temperatureDerate = Math.Clamp(1 - Math.Max(0, cellTemperatureC - 25d) * 0.0034, 0.86, 1.02);
        var irradianceRatio = Math.Clamp(transposedIrradiance / 1000d, 0, 1.18);
        var estimate = installedPvKw * irradianceRatio * orientationGain * inverterDerate * soilingDerate * cableDerate * rainDerate * cloudFineTune * temperatureDerate;

        return Math.Round(Math.Clamp(estimate, 0, installedPvKw * 1.03), 2);
    }

    private static double EstimateDailyKwh(IReadOnlyList<WeatherHourPoint> points)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        return Math.Round(points.Sum(x => x.EstimatedPvKw), 1);
    }

    private static string DescribeCondition(double cloudCoverPct, double precipitationMm, double radiationWm2)
    {
        if (precipitationMm >= 0.5)
        {
            return "Dazdive";
        }

        if (cloudCoverPct >= 80)
        {
            return "Zamracene";
        }

        if (cloudCoverPct >= 45)
        {
            return "Polooblacno";
        }

        return radiationWm2 > 250 ? "Slnecno" : "Jasne/prechodne";
    }

    private static string BuildSummary(double estimatedKwh, double peakPvKw, double installedPvKw, double cloudCoverPct, double precipitationMm)
    {
        var limitPct = installedPvKw > 0 ? peakPvKw / installedPvKw * 100 : 0;
        var weatherNote = precipitationMm > 0.2
            ? "Dazd moze znizit realnu vyrobu."
            : cloudCoverPct >= 70
                ? "Oblacnost bude hlavny limit."
                : "Podmienky vyzeraju pouzitelne pre FV.";

        return $"Odhad dnes {estimatedKwh:0.0} kWh, spicka asi {peakPvKw:0.0} kW ({limitPct:0} % instalacie). {weatherNote}";
    }

    private static string FormatTime(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed.ToString("HH:mm")
            : value;
    }

    private sealed record CacheEntry(DateTimeOffset CreatedUtc, WeatherForecastSummary Value);

    private sealed record GeoLocation(double Latitude, double Longitude, string DisplayName);

    private sealed class NominatimResult
    {
        public string? Lat { get; init; }
        public string? Lon { get; init; }
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }
    }

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("hourly")]
        public OpenMeteoHourly? Hourly { get; init; }
        [JsonPropertyName("daily")]
        public OpenMeteoDaily? Daily { get; init; }
    }

    private sealed class OpenMeteoHourly
    {
        [JsonPropertyName("time")]
        public string[] Time { get; init; } = [];
        [JsonPropertyName("temperature_2m")]
        public double[] Temperature { get; init; } = [];
        [JsonPropertyName("cloud_cover")]
        public double[] CloudCover { get; init; } = [];
        [JsonPropertyName("precipitation")]
        public double[] Precipitation { get; init; } = [];
        [JsonPropertyName("wind_speed_10m")]
        public double[] WindSpeed { get; init; } = [];
        [JsonPropertyName("shortwave_radiation")]
        public double[] ShortwaveRadiation { get; init; } = [];
        [JsonPropertyName("direct_radiation")]
        public double[] DirectRadiation { get; init; } = [];
        [JsonPropertyName("diffuse_radiation")]
        public double[] DiffuseRadiation { get; init; } = [];
    }

    private sealed class OpenMeteoDaily
    {
        [JsonPropertyName("sunrise")]
        public string[] Sunrise { get; init; } = [];
        [JsonPropertyName("sunset")]
        public string[] Sunset { get; init; } = [];
    }
}
