using NestStats2.Models;

namespace NestStats2.Services;

public interface IEnergyDashboardService
{
    Task<IReadOnlyList<SystemInfo>> GetSystemsAsync(CancellationToken cancellationToken = default);

    Task<bool> SystemExistsAsync(string snNumber, CancellationToken cancellationToken = default);

    Task<DashboardData> GetDashboardAsync(
        string? snNumber,
        DateTime? day,
        int hoursBack,
        IReadOnlyCollection<string>? allowedSnNumbers = null,
        IProgress<DashboardLoadProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyHistoryPoint>> GetHistoryAsync(
        string snNumber,
        DateTime? referenceDay,
        int? days,
        IReadOnlyCollection<string>? allowedSnNumbers = null,
        CancellationToken cancellationToken = default);

    Task<LiveSnapshot?> GetLiveSnapshotAsync(string snNumber, CancellationToken cancellationToken = default);
}
