using NestStats2.Data;

namespace NestStats2.Services;

public sealed class StartupInitializationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StartupInitializationState _state;
    private readonly ILogger<StartupInitializationHostedService> _logger;

    public StartupInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        StartupInitializationState state,
        ILogger<StartupInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _state.Report("Startup", "Preparing application services.");
            await Task.Yield();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();

            _state.Report("Identity", "Initializing local auth database and roles.");
            await seeder.SeedAsync(stoppingToken);

            _state.MarkReady();
            _logger.LogInformation("Startup initialization completed.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Startup initialization canceled.");
        }
        catch (Exception ex)
        {
            _state.MarkFailed(ex);
            _logger.LogError(ex, "Startup initialization failed.");
        }
    }
}
