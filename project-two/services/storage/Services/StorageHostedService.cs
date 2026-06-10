namespace StorageService.Services;

public sealed class StorageHostedService(IStorageWorker worker) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => worker.RunAsync(stoppingToken);
}

