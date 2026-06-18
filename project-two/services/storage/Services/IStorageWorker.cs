namespace StorageService.Services;

public interface IStorageWorker
{
    Task RunAsync(CancellationToken cancellationToken);
}

