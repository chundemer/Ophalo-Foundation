using OpHalo.Foundation.Application.Abstractions.Security;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Placeholder ICurrentUser for the API host until Phase 5 auth exists (ADR-058).
/// Every property returns the unauthenticated/empty sentinel so service-layer auth
/// guards fail closed by default.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid UserId => Guid.Empty;
    public Guid AccountId => Guid.Empty;
    public bool IsAuthenticated => false;
    public bool IsVerified => false;
}
