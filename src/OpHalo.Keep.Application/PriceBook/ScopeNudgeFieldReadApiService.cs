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

/// <summary>One surviving suggestion — price-free, no eligibility/repair field (an ineligible or
/// already-drafted suggestion is omitted entirely, never marked) — build-log/123.</summary>
public sealed record ScopeNudgeSuggestionFieldRow(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string DisplayName,
    ScopeNudgeTargetKind TargetKind);

/// <summary>Result of a field nudge-read. <see cref="RuleId"/> is null and <see cref="Suggestions"/>
/// is empty when no rule is configured for the supplied trigger, the trigger target is no longer
/// eligible, or every suggestion was filtered out (build-log/123 "returns an empty result").</summary>
public sealed record ScopeNudgeFieldResult(
    Guid? RuleId,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ScopeNudgeSuggestionFieldRow> Suggestions)
{
    public static readonly ScopeNudgeFieldResult Empty = new(null, null, null, []);
}

/// <summary>
/// API-facing orchestration for the technician-reachable, scope-bound, price-free Paired Nudges
/// field read (Session 3, build-log/123): given one direct trigger, the ordered surviving
/// suggestions for the caller's open <c>ProposedScope</c>. Gate composition and the load-scope-then-
/// verify-request-visibility ordering mirror <see cref="ProposedScopeReadApiService.GetByIdAsync"/>
/// exactly (read-only gate 1 — Blocked-only denies — not the mutation Blocked||ReadOnly posture).
/// Gate composition also includes the BL142 Session 1 server-owned release gate
/// (<see cref="IReleaseGatePolicy"/>, ADR-496). Read-only: no concurrency token, no terminal-state
/// gate, no Draft mutation.
/// </summary>
public sealed class ScopeNudgeFieldReadApiService(
    IProposedScopePersistence proposedScopePersistence,
    IScopeNudgeRulePersistence rulePersistence,
    IKeepRequestDetailPersistence requestPersistence,
    ICatalogReadPersistence catalogReadPersistence,
    IOfferingAssemblyPersistence offeringAssemblyPersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IReleaseGatePolicy releaseGatePolicy,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    /// <summary>
    /// <paramref name="triggerCatalogItemIdValues"/>/<paramref name="triggerOfferingAssemblyIdValues"/>
    /// are the raw, unvalidated query-string values for each parameter name (zero, one, or more —
    /// however many the caller supplied). Shape validation (missing/duplicate/combined/malformed
    /// all collapse to <see cref="ScopeNudgeRuleErrors.TriggerQueryParameterInvalid"/>) deliberately
    /// runs after every auth gate and the request-visibility check, not at the route layer — build-
    /// log/123's gates -> request visibility -> evaluate-trigger ordering, so an unauthenticated or
    /// unauthorized caller never learns anything about trigger-parameter shape.
    /// </summary>
    public async Task<Result<ScopeNudgeFieldResult>> GetSuggestionsAsync(
        Guid proposedScopeId,
        IReadOnlyList<string> triggerCatalogItemIdValues,
        IReadOnlyList<string> triggerOfferingAssemblyIdValues,
        CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ScopeNudgeFieldResult>.Failure(gate.Error);

        var scope = await proposedScopePersistence.GetByIdAsync(currentUser.AccountId, proposedScopeId, ct);
        if (scope is null)
            return Result<ScopeNudgeFieldResult>.Failure(ProposedScopeErrors.NotFound);

        var request = await requestPersistence.GetRequestAsync(
            scope.RequestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (request is null)
            return Result<ScopeNudgeFieldResult>.Failure(KeepRequestErrors.NotFound);

        var triggerResult = ParseTrigger(triggerCatalogItemIdValues, triggerOfferingAssemblyIdValues);
        if (triggerResult.IsFailure)
            return Result<ScopeNudgeFieldResult>.Failure(triggerResult.Error);

        var (triggerCatalogItemId, triggerOfferingAssemblyId) = triggerResult.Value;

        var rule = await rulePersistence.GetByTriggerAsync(
            currentUser.AccountId, triggerCatalogItemId, triggerOfferingAssemblyId, ct);
        if (rule is null)
            return Result<ScopeNudgeFieldResult>.Success(ScopeNudgeFieldResult.Empty);

        var (_, triggerIsEligible) = await ResolveTargetAsync(rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId, ct);
        if (!triggerIsEligible)
            return Result<ScopeNudgeFieldResult>.Success(ScopeNudgeFieldResult.Empty);

        var draftLineTargets = scope.Lines
            .Select(l => (l.CatalogItemId, l.OfferingAssemblyId))
            .ToHashSet();

        var rows = new List<ScopeNudgeSuggestionFieldRow>();
        foreach (var suggestion in rule.Suggestions.OrderBy(s => s.Order))
        {
            if (draftLineTargets.Contains((suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId)))
                continue;

            var (displayName, isEligible) = await ResolveTargetAsync(
                suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId, ct);
            if (!isEligible)
                continue;

            var targetKind = suggestion.SuggestedCatalogItemId is not null
                ? ScopeNudgeTargetKind.CatalogItem
                : ScopeNudgeTargetKind.OfferingAssembly;

            rows.Add(new ScopeNudgeSuggestionFieldRow(
                suggestion.Id, suggestion.Order, suggestion.SuggestedCatalogItemId,
                suggestion.SuggestedOfferingAssemblyId, displayName, targetKind));
        }

        if (rows.Count == 0)
            return Result<ScopeNudgeFieldResult>.Success(ScopeNudgeFieldResult.Empty);

        return Result<ScopeNudgeFieldResult>.Success(
            new ScopeNudgeFieldResult(rule.Id, rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId, rows));
    }

    /// <summary>Missing, duplicate, combined, or malformed trigger parameters all fail as the same
    /// <see cref="ScopeNudgeRuleErrors.TriggerQueryParameterInvalid"/> — the client-facing contract
    /// (build-log/123) does not distinguish the wire-shape reason.</summary>
    private static Result<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> ParseTrigger(
        IReadOnlyList<string> triggerCatalogItemIdValues, IReadOnlyList<string> triggerOfferingAssemblyIdValues)
    {
        if (triggerCatalogItemIdValues.Count > 1 || triggerOfferingAssemblyIdValues.Count > 1)
            return Result<(Guid?, Guid?)>.Failure(ScopeNudgeRuleErrors.TriggerQueryParameterInvalid);

        var hasCatalogTrigger = triggerCatalogItemIdValues.Count == 1;
        var hasAssemblyTrigger = triggerOfferingAssemblyIdValues.Count == 1;
        if (hasCatalogTrigger == hasAssemblyTrigger)
            return Result<(Guid?, Guid?)>.Failure(ScopeNudgeRuleErrors.TriggerQueryParameterInvalid);

        if (hasCatalogTrigger)
        {
            if (!Guid.TryParse(triggerCatalogItemIdValues[0], out var catalogItemId) || catalogItemId == Guid.Empty)
                return Result<(Guid?, Guid?)>.Failure(ScopeNudgeRuleErrors.TriggerQueryParameterInvalid);

            return Result<(Guid?, Guid?)>.Success((catalogItemId, null));
        }

        if (!Guid.TryParse(triggerOfferingAssemblyIdValues[0], out var offeringAssemblyId) || offeringAssemblyId == Guid.Empty)
            return Result<(Guid?, Guid?)>.Failure(ScopeNudgeRuleErrors.TriggerQueryParameterInvalid);

        return Result<(Guid?, Guid?)>.Success((null, offeringAssemblyId));
    }

    /// <summary>Eligibility means Active <c>CatalogItem</c> or operationally eligible
    /// <c>OfferingAssembly</c>; neither is limited to Common Items (build-log/123) — same logic as
    /// <see cref="ScopeNudgeRuleConfigApiService"/>'s identical private method, restated here per
    /// this codebase's established per-service duplication (matches
    /// <see cref="QuickScopeActionFieldReadApiService"/> vs <see cref="QuickScopeActionConfigApiService"/>).</summary>
    private async Task<(string DisplayName, bool IsEligible)> ResolveTargetAsync(
        Guid? catalogItemId, Guid? offeringAssemblyId, CancellationToken ct)
    {
        if (catalogItemId is { } id)
        {
            var detail = await catalogReadPersistence.GetItemDetailAsync(currentUser.AccountId, id, ct);
            var isEligible = detail is not null && detail.Item.ActiveState == CatalogItemActiveState.Active;
            return (detail?.Item.DisplayName ?? "(deleted item)", isEligible);
        }

        var offeringAssemblyDetail = await offeringAssemblyPersistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId!.Value, ct);
        var assemblyIsEligible = offeringAssemblyDetail is not null
            && offeringAssemblyDetail.ActiveState == CatalogActiveState.Active
            && offeringAssemblyDetail.Eligibility.IsEligible;
        return (offeringAssemblyDetail?.Name ?? "(deleted assembly)", assemblyIsEligible);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success — read-only gate
    /// 1 (Blocked-only denies), matching <see cref="ProposedScopeReadApiService"/>.</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

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
        if (decision.IsBlocked)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

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
