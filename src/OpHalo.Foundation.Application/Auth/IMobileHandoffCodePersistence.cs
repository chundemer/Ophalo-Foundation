using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Persistence seam for mobile handoff code creation and atomic redemption.
/// </summary>
public interface IMobileHandoffCodePersistence
{
    Task CreateAsync(MobileHandoffCode code, CancellationToken cancellationToken);

    Task<MobileHandoffCode?> FindByHashAsync(string codeHash, CancellationToken cancellationToken);

    Task<bool> ConsumeAsync(Guid codeId, DateTime consumedAtUtc, CancellationToken cancellationToken);
}
