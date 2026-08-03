using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Persistence seam for <see cref="ManualPriceOverride"/> — an append-only audit row with no
/// update path, so this seam is insert-only.
/// </summary>
public interface IManualPriceOverridePersistence
{
    Task AddAsync(ManualPriceOverride entry, CancellationToken ct);
}
