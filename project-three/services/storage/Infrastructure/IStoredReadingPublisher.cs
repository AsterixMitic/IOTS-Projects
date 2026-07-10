using StorageService.Models;

namespace StorageService.Infrastructure;

/// <summary>
/// Apstrakcija za re-publikovanje perzistiranih očitavanja na broker (Projekat 3).
/// Konvencija je ista kao kod <see cref="StorageService.Services.IStorageWorker"/> —
/// worker zavisi od interfejsa, a ne od konkretne implementacije.
/// </summary>
public interface IStoredReadingPublisher
{
    Task PublishAsync(IReadOnlyList<QueuedReading> batch, CancellationToken cancellationToken);
}
