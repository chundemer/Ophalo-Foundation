using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateProposedScopeApiCommand(Guid RequestId);

public sealed record AddProposedScopeLineApiCommand(
    ProposedScopeLineType LineType,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    decimal Quantity,
    bool IsException,
    string? OffCatalogDescription,
    decimal? OffCatalogQuantity,
    string? Note,
    int DisplayOrder,
    string DisplayNameSnapshot,
    string? UnitOfMeasureSnapshot,
    string? OfferingAssemblyNameSnapshot,
    decimal? DefaultQuantitySnapshot);

public sealed record UpdateProposedScopeLineApiCommand(decimal Quantity, bool IsException, string? Note, int DisplayOrder);

/// <summary>
/// API-facing orchestration for every <see cref="ProposedScope"/> mutation (Session 3.3b),
/// including submit — the single ADR-480 three-gate owner: <c>RequestsOperate</c> (B2 operator
/// gate) + Price Book entitlement (ADR-462) + <c>ScopeCapture</c> (ADR-480). Gate 1/2 mirror
/// <see cref="OfferingAssemblyApiService"/> exactly; gate 3 checks two permissions instead of one,
/// since ProposedScope capture is gated on both the general operator-write permission and the
/// dedicated scope-capture permission. <see cref="SubmitProposedScopeService"/> stays exactly as
/// Session 3.3a.2 left it — thin, auth-free — so gates are never evaluated twice.
/// </summary>
public sealed class ProposedScopeApiService(
    CreateProposedScopeService createService,
    EditProposedScopeService editService,
    SubmitProposedScopeService submitService,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<ProposedScope>> CreateAsync(CreateProposedScopeApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ProposedScope>.Failure(gate.Error);

        return await createService.CreateAsync(
            new CreateProposedScopeCommand(
                currentUser.AccountId, command.RequestId, currentUser.UserId, gate.Value, currentUser.UserId),
            ct);
    }

    public async Task<Result<AddProposedScopeLineResult>> AddLineAsync(
        Guid proposedScopeId, AddProposedScopeLineApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<AddProposedScopeLineResult>.Failure(gate.Error);

        return await editService.AddLineAsync(
            new AddProposedScopeLineCommand(
                currentUser.AccountId, proposedScopeId, expectedVersion, currentUser.UserId, gate.Value,
                command.LineType, command.CatalogItemId, command.OfferingAssemblyId, command.Quantity,
                command.IsException, command.OffCatalogDescription, command.OffCatalogQuantity, command.Note,
                command.DisplayOrder, command.DisplayNameSnapshot, command.UnitOfMeasureSnapshot,
                command.OfferingAssemblyNameSnapshot, command.DefaultQuantitySnapshot, currentUser.UserId),
            ct);
    }

    public async Task<Result<Guid>> UpdateLineAsync(
        Guid proposedScopeId, Guid lineId, UpdateProposedScopeLineApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await editService.UpdateLineAsync(
            new UpdateProposedScopeLineCommand(
                currentUser.AccountId, proposedScopeId, lineId, expectedVersion, currentUser.UserId, gate.Value,
                command.Quantity, command.IsException, command.Note, command.DisplayOrder),
            ct);
    }

    public async Task<Result<Guid>> RemoveLineAsync(Guid proposedScopeId, Guid lineId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await editService.RemoveLineAsync(
            new RemoveProposedScopeLineCommand(
                currentUser.AccountId, proposedScopeId, lineId, expectedVersion, currentUser.UserId, gate.Value),
            ct);
    }

    public async Task<Result<Guid>> SubmitAsync(Guid proposedScopeId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        // Row-authorization check: SubmitProposedScopeService/IProposedScopeSubmissionPersistence
        // are account-scoped only (ADR-463's atomic transaction has no row-visibility concept), so
        // this gate must reject an Operator's MyWork-inaccessible scope here — otherwise gate.Value
        // would be discarded and any account member could submit any scope in the account.
        var visibility = await editService.VerifyRequestVisibleAsync(
            currentUser.AccountId, proposedScopeId, currentUser.UserId, gate.Value, ct);
        if (visibility.IsFailure)
            return Result<Guid>.Failure(visibility.Error);

        return await submitService.SubmitAsync(currentUser.AccountId, proposedScopeId, expectedVersion, ct);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success, so callers can
    /// pass it straight into the terminal-request read without a second role lookup.</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

        // Gate 1 — account access (commercial/lifecycle). A mutation, so both Blocked and
        // ReadOnly (e.g. OffSeason) deny.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 3 — user permissions: RequestsOperate (B2 operator-write gate) AND ScopeCapture
        // (ADR-480) — two independent permission checks, not a single combined key.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ScopeCapture))
        {
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);
        }

        var scope = roleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<KeepRequestVisibilityScope>.Success(scope);
    }
}
