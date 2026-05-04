using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NestStats2.Models;

namespace NestStats2.Services;

public interface IDashboardLoadCoordinator
{
    string StartJob(DashboardLoadRequest request);

    DashboardLoadSnapshot? GetSnapshot(string jobId, string userId);

    DashboardData? TryGetPreparedDashboard(string jobId, string userId);
}

public sealed record DashboardLoadRequest(
    string UserId,
    string? SnNumber,
    DateTime? Day,
    int HoursBack,
    IReadOnlyCollection<string>? AllowedSnNumbers,
    bool IsAdmin);

public sealed record DashboardLoadSnapshot(
    string JobId,
    int Percent,
    string Stage,
    string Detail,
    bool Completed,
    bool Failed,
    string? Error);

public sealed class DashboardLoadCoordinator : IDashboardLoadCoordinator
{
    private static readonly TimeSpan JobLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(75);

    private readonly ConcurrentDictionary<string, DashboardLoadJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DashboardLoadCoordinator> _logger;

    public DashboardLoadCoordinator(IServiceScopeFactory scopeFactory, ILogger<DashboardLoadCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string StartJob(DashboardLoadRequest request)
    {
        CleanupExpiredJobs();

        var jobId = Guid.NewGuid().ToString("N");
        var state = new DashboardLoadJobState
        {
            JobId = jobId,
            UserId = request.UserId,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Percent = 2,
            Stage = "Priprava",
            Detail = "Spustam nacitanie dashboardu."
        };

        _jobs[jobId] = state;

        _logger.LogInformation(
            "Dashboard load job {JobId} started for user {UserId}, system {SnNumber}, day {Day}, hours {HoursBack}",
            jobId,
            request.UserId,
            request.SnNumber,
            request.Day,
            request.HoursBack);

        _ = Task.Run(() => RunJobAsync(state, request));
        return jobId;
    }

    public DashboardLoadSnapshot? GetSnapshot(string jobId, string userId)
    {
        CleanupExpiredJobs();

        if (!_jobs.TryGetValue(jobId, out var state) ||
            !string.Equals(state.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            return new DashboardLoadSnapshot(
                state.JobId,
                state.Percent,
                state.Stage,
                state.Detail,
                state.Completed,
                state.Failed,
                state.Error);
        }
    }

    public DashboardData? TryGetPreparedDashboard(string jobId, string userId)
    {
        CleanupExpiredJobs();

        if (!_jobs.TryGetValue(jobId, out var state) ||
            !string.Equals(state.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            if (!state.Completed || state.Failed)
            {
                return null;
            }

            return state.PreparedDashboard;
        }
    }

    private async Task RunJobAsync(DashboardLoadJobState state, DashboardLoadRequest request)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            using var timeout = new CancellationTokenSource(JobTimeout);
            var cancellationToken = timeout.Token;
            var dashboardService = scope.ServiceProvider.GetRequiredService<IEnergyDashboardService>();
            var weatherForecastService = scope.ServiceProvider.GetRequiredService<IWeatherForecastService>();

            var progress = new Progress<DashboardLoadProgressUpdate>(update =>
            {
                UpdateState(state, update.Percent, update.Stage, update.Detail);
            });

            UpdateState(state, 5, "System", "Overujem system a pripravujem datove okno.");
            var dashboard = await dashboardService.GetDashboardAsync(
                request.SnNumber,
                request.Day,
                request.HoursBack,
                request.IsAdmin ? null : request.AllowedSnNumbers,
                progress,
                cancellationToken);

            UpdateState(state, 90, "Pocasie", "Doplnam pocasie a odhad vyroby.");
            dashboard.Weather = await weatherForecastService.GetForecastAsync(
                dashboard.SystemAddress,
                dashboard.InstalledPvKw,
                cancellationToken);

            UpdateState(state, 97, "Render", "Dokoncujem data pre zobrazenie.");

            lock (state.SyncRoot)
            {
                state.PreparedDashboard = dashboard;
                state.Percent = 100;
                state.Stage = "Hotovo";
                state.Detail = "Dashboard je pripraveny.";
                state.Completed = true;
                state.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation("Dashboard load job {JobId} completed", state.JobId);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Dashboard load job {JobId} timed out after {TimeoutSeconds} seconds", state.JobId, JobTimeout.TotalSeconds);
            lock (state.SyncRoot)
            {
                state.Percent = 100;
                state.Stage = "Timeout";
                state.Detail = "Nacitanie dashboardu trvalo prilis dlho. Skontroluj Supabase konfiguraciu, dostupnost tabuliek alebo skus mensi casovy rozsah.";
                state.Error = "Dashboard load timed out.";
                state.Failed = true;
                state.Completed = true;
                state.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard load job {JobId} failed", state.JobId);
            lock (state.SyncRoot)
            {
                state.Percent = Math.Max(state.Percent, 100);
                state.Stage = "Chyba";
                state.Detail = "Nacitanie dashboardu zlyhalo.";
                state.Error = ex.Message;
                state.Failed = true;
                state.Completed = true;
                state.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private static void UpdateState(DashboardLoadJobState state, int percent, string stage, string detail)
    {
        lock (state.SyncRoot)
        {
            state.Percent = Math.Clamp(percent, 0, 100);
            state.Stage = stage;
            state.Detail = detail;
            state.UpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    private void CleanupExpiredJobs()
    {
        var threshold = DateTimeOffset.UtcNow - JobLifetime;
        foreach (var pair in _jobs)
        {
            if (pair.Value.UpdatedUtc < threshold)
            {
                _jobs.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class DashboardLoadJobState
    {
        public object SyncRoot { get; } = new();
        public string JobId { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public int Percent { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public string? Error { get; set; }
        public DashboardData? PreparedDashboard { get; set; }
    }
}
