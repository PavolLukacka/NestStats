using NestStats2.Models;

namespace NestStats2.Services;

public interface ISpotMarketPriceService
{
    Task<SpotMarketSummary> GetSkDayAheadSummaryAsync(DateTime localToday, CancellationToken cancellationToken = default);
}
