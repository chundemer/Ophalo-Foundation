using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Field-select command (Session 3.4d, build-log/118 decision 2): only
/// <see cref="ProposedScopeLineType.KnownCatalogItem"/> or <see cref="ProposedScopeLineType.OffCatalogItem"/>.
/// No DisplayOrder (server-computed) and no display snapshots (server-resolved) - the caller only
/// ever supplies raw selection intent.</summary>
public sealed record FieldSelectProposedScopeLineApiCommand(
    ProposedScopeLineType LineType,
    Guid? CatalogItemId,
    decimal Quantity,
    string? OffCatalogDescription,
    string? Note);

/// <summary>
/// API-facing orchestration for the technician-reachable, server-authoritative single-line selection
/// endpoint (Session 3.4d, build-log/118): resolves <c>KnownCatalogItem</c> against the account-owned,
/// Active catalog server-side (never trusting a caller-supplied display snapshot - build-log/118 "Why
/// 3.3b is not enough" #2), and derives the off-catalog <c>DisplayNameSnapshot</c> server-side from
/// <c>OffCatalogDescription</c> (decision 6). Sits beside <see cref="ProposedScopeApiService"/> as the
/// sole allowed path to append a line - the raw <c>POST .../lines</c> endpoint this replaces is
/// retired in the same commit, not re-gated. Gate composition (including the BL142 Session 1
/// server-owned release gate, <see cref="IReleaseGatePolicy"/>, ADR-496) and row-visibility
/// ordering match <see cref="ProposedScopeApiService"/> exactly, except visibility is checked (via
/// <see cref="EditProposedScopeService.VerifyRequestVisibleAsync"/>) before any catalog-item
/// resolution, so an invisible scope 404s before a referenced item's existence/active state is ever
/// evaluated - build-log/118's locked gates -> request visibility -> act ordering.
/// </summary>
public sealed class FieldProposedScopeSelectionApiService(
    EditProposedScopeService editService,
    ICatalogReadPersistence catalogPersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IReleaseGatePolicy releaseGatePolicy,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private const int MaxDisplayNameSnapshotLength = 200;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<AddProposedScopeLineResult>> AddLineAsync(
        Guid proposedScopeId, FieldSelectProposedScopeLineApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        if (command.LineType is not (ProposedScopeLineType.KnownCatalogItem or ProposedScopeLineType.OffCatalogItem))
            return Result<AddProposedScopeLineResult>.Failure(ProposedScopeErrors.FieldSelectLineTypeInvalid);

        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<AddProposedScopeLineResult>.Failure(gate.Error);

        // Row visibility before any catalog-item resolution - an invisible scope must 404 before a
        // referenced item's existence/active state is ever evaluated.
        var visibility = await editService.VerifyRequestVisibleAsync(
            currentUser.AccountId, proposedScopeId, currentUser.UserId, gate.Value, ct);
        if (visibility.IsFailure)
            return Result<AddProposedScopeLineResult>.Failure(visibility.Error);

        string displayNameSnapshot;
        string? unitOfMeasureSnapshot = null;
        decimal? offCatalogQuantity = null;

        if (command.LineType == ProposedScopeLineType.KnownCatalogItem)
        {
            if (command.CatalogItemId is null)
                return Result<AddProposedScopeLineResult>.Failure(ProposedScopeErrors.LineCatalogItemRequired);

            var detail = await catalogPersistence.GetItemDetailAsync(currentUser.AccountId, command.CatalogItemId.Value, ct);
            if (detail is null || detail.Item.ActiveState != CatalogItemActiveState.Active)
                return Result<AddProposedScopeLineResult>.Failure(ProposedScopeErrors.LineCatalogItemNotFound);

            displayNameSnapshot = detail.Item.DisplayName;
            unitOfMeasureSnapshot = detail.Item.UnitOfMeasure;
        }
        else
        {
            var snapshotResult = DeriveOffCatalogDisplayNameSnapshot(command.OffCatalogDescription);
            if (snapshotResult.IsFailure)
                return Result<AddProposedScopeLineResult>.Failure(snapshotResult.Error);

            displayNameSnapshot = snapshotResult.Value;
            offCatalogQuantity = command.Quantity;
        }

        return await editService.AppendFieldLineAsync(
            new AppendProposedScopeLineCommand(
                currentUser.AccountId, proposedScopeId, expectedVersion, currentUser.UserId, gate.Value,
                command.LineType, command.CatalogItemId, command.Quantity, command.OffCatalogDescription,
                offCatalogQuantity, command.Note, displayNameSnapshot, unitOfMeasureSnapshot, currentUser.UserId),
            ct);
    }

    /// <summary>Trim, then reject any C0/C1 control character (U+0000-U+001F, U+007F-U+009F - a
    /// single-line field, so embedded tab/newline/CR are rejected too), then truncate to 200 chars.
    /// No broader Unicode sanitization (build-log/118 decision 6). The full, untruncated description
    /// is stored separately in <c>OffCatalogDescription</c> - this method only derives the display
    /// name.</summary>
    private static Result<string> DeriveOffCatalogDisplayNameSnapshot(string? offCatalogDescription)
    {
        if (string.IsNullOrWhiteSpace(offCatalogDescription))
            return Result<string>.Failure(ProposedScopeErrors.LineOffCatalogDescriptionRequired);

        var trimmed = offCatalogDescription.Trim();

        foreach (var c in trimmed)
        {
            var isC0Control = c <= '\u001F';
            var isC1Control = c >= '\u007F' && c <= '\u009F';
            if (isC0Control || isC1Control)
                return Result<string>.Failure(ProposedScopeErrors.LineOffCatalogDescriptionInvalidCharacters);
        }

        var snapshot = trimmed.Length > MaxDisplayNameSnapshotLength ? trimmed[..MaxDisplayNameSnapshotLength] : trimmed;
        return Result<string>.Success(snapshot);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success - same contract as
    /// <see cref="ProposedScopeApiService"/>'s identical private method, restated here per
    /// build-log/118 ("stated explicitly, not inherited by reference").</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

        // Gate 1 - account access (commercial/lifecycle). A mutation, so both Blocked and
        // ReadOnly (e.g. OffSeason) deny - matches ProposedScopeApiService's mutation posture.
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

        // Gate 2 - Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 2b - server-owned release gate (BL142 Session 1, ADR-496): entitlement alone never
        // exposes Proposed Work before it is explicitly released. Fails closed independently of
        // gate 2 above.
        if (!releaseGatePolicy.IsProposedWorkReleased())
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 3 - user permissions: RequestsOperate (B2 operator-write gate) AND ScopeCapture
        // (ADR-480) - two independent permission checks, not a single combined key.
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
