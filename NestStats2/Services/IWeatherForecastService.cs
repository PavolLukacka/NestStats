using NestStats2.Models;

namespace NestStats2.Services;

public interface IWeatherForecastService
{
    Task<WeatherForecastSummary> GetForecastAsync(
        string? address,
        double installedPvKw,
        CancellationToken cancellationToken = default);
}
