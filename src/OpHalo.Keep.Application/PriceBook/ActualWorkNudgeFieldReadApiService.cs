using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One surviving suggestion — price-free, no eligibility/repair field (an ineligible or
/// already-added suggestion is omitted entirely, never marked), matching
/// <see cref="ScopeNudgeSuggestionFieldRow"/>'s contract shape.</summary>
public sealed record ActualWorkNudgeSuggestionFieldRow(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string DisplayName);

/// <summary>Result of a field nudge-read for one Actual Work Draft trigger. <see cref="RuleId"/> is
/// null and <see cref="Suggestions"/> is empty when no rule is configured for the supplied trigger,
/// the trigger target is no longer eligible, or every suggestion was filtered out.</summary>
public sealed record ActualWorkNudgeFieldResult(
    Guid? RuleId,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ActualWorkNudgeSuggestionFieldRow> Suggestions)
{
    public static readonly ActualWorkNudgeFieldResult Empty = new(null, null, null, []);
}

/// <summary>
/// API-facing orchestration for the technician-reachable, Actual-Work-Draft-bound, price-free
/// Actual Work nudge field read (build-log/129, 5d-ii-c): given one direct trigger, the ordered
/// surviving suggestions for the caller's open <c>ActualWork</c> Draft. Explicit add reuses
/// <see cref="ActualWorkDraftApiService.AddLineAsync"/> directly — no new mutation handler here.
///
/// Gate composition mirrors <see cref="ActualWorkDraftApiService"/>'s draft-mutation gates exactly
/// (<c>RequestsOperate</c> + Price Book entitlement + <c>ActualWorkCapture</c>) plus row
/// authorization via <see cref="IActiveResponsibleCheck"/> — a non-Responsible caller for the
/// Draft's request gets the same indistinguishable <see cref="KeepRequestErrors.NotFound"/> as
/// <see cref="ActualWorkDraftApiService.AuthorizeAndLoadDraftAsync"/> — because the Draft is
/// exclusive to its active Responsible participant, unlike <c>ProposedScope</c>'s broader
/// row-visibility read. Account posture is Blocked-only (not Blocked||ReadOnly): this is
/// price-blind, non-mutating availability data; a read-only account may still see suggestions even
/// though the later add action is unavailable until the account leaves read-only.
/// </summary>
public sealed class ActualWorkNudgeFieldReadApiService(
    IActualWorkPersistence actualWorkPersistence,
    IActualWorkNudgeRulePersistence rulePersistence,
    IActiveResponsibleCheck responsibleCheck,
    ICatalogReadPersistence catalogReadPersistence,
    IOfferingAssemblyPersistence offeringAssemblyPersistence,
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

    /// <summary>
    /// <paramref name="triggerCatalogItemIdValues"/>/<paramref name="triggerOfferingAssemblyIdValues"/>
    /// are the raw, unvalidated query-string values for each parameter name. Shape validation
    /// (missing/duplicate/combined/malformed all collapse to
    /// <see cref="ScopeNudgeRuleErrors.TriggerQueryParameterInvalid"/>) deliberately runs after every
    /// auth gate and the Draft/active-Responsible load, matching
    /// <see cref="ScopeNudgeFieldReadApiService.GetSuggestionsAsync"/>'s gates -> load -> evaluate-
    /// trigger ordering.
    /// </summary>
    public async Task<Result<ActualWorkNudgeFieldResult>> GetSuggestionsAsync(
        Guid actualWorkId,
        IReadOnlyList<string> triggerCatalogItemIdValues,
        IReadOnlyList<string> triggerOfferingAssemblyIdValues,
        CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<ActualWorkNudgeFieldResult>.Failure(loadResult.Error);

        var actualWork = loadResult.Value;

        var triggerResult = ParseTrigger(triggerCatalogItemIdValues, triggerOfferingAssemblyIdValues);
        if (triggerResult.IsFailure)
            return Result<ActualWorkNudgeFieldResult>.Failure(triggerResult.Error);

        var (triggerCatalogItemId, triggerOfferingAssemblyId) = triggerResult.Value;

        var rule = await rulePersistence.GetByTriggerAsync(
            currentUser.AccountId, triggerCatalogItemId, triggerOfferingAssemblyId, ct);
        if (rule is null)
            return Result<ActualWorkNudgeFieldResult>.Success(ActualWorkNudgeFieldResult.Empty);

        var (_, triggerIsEligible) = await ResolveTargetAsync(rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId, ct);
        if (!triggerIsEligible)
            return Result<ActualWorkNudgeFieldResult>.Success(ActualWorkNudgeFieldResult.Empty);

        // ActualWorkLine carries no OfferingAssemblyId — assembly provenance is not retained after
        // expansion — so only catalog-item suggestions can be matched against the Draft's existing
        // lines. Assembly suggestions are never suppressed here; partial/full prior expansion is
        // reported by the existing expand-assembly endpoint's skip-and-report result instead.
        var draftCatalogItemIds = actualWork.Lines
            .Select(l => l.CatalogItemId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var rows = new List<ActualWorkNudgeSuggestionFieldRow>();
        foreach (var suggestion in rule.Suggestions.OrderBy(s => s.Order))
        {
            if (suggestion.SuggestedCatalogItemId is { } suggestedCatalogItemId
                && draftCatalogItemIds.Contains(suggestedCatalogItemId))
                continue;

            var (displayName, isEligible) = await ResolveTargetAsync(
                suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId, ct);
            if (!isEligible)
                continue;

            rows.Add(new ActualWorkNudgeSuggestionFieldRow(
                suggestion.Id, suggestion.Order, suggestion.SuggestedCatalogItemId,
                suggestion.SuggestedOfferingAssemblyId, displayName));
        }

        if (rows.Count == 0)
            return Result<ActualWorkNudgeFieldResult>.Success(ActualWorkNudgeFieldResult.Empty);

        return Result<ActualWorkNudgeFieldResult>.Success(
            new ActualWorkNudgeFieldResult(rule.Id, rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId, rows));
    }

    /// <summary>Missing, duplicate, combined, or malformed trigger parameters all fail as the same
    /// <see cref="ScopeNudgeRuleErrors.TriggerQueryParameterInvalid"/> — reused from the ScopeNudge
    /// contract rather than duplicated, since the wire shape is identical.</summary>
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
    /// <c>OfferingAssembly</c> — same logic as <see cref="ActualWorkNudgeRuleConfigApiService"/>'s
    /// identical private method, restated here per this codebase's established per-service
    /// duplication.</summary>
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

    /// <summary>Gate 1-3, then load the visit and confirm it is still a Draft owned (by request) by
    /// the caller's active Responsible participation — the same row-authorization check
    /// <see cref="ActualWorkDraftApiService.AuthorizeAndLoadDraftAsync"/> shares across every line
    /// mutation, reused here for the read path instead of ScopeNudge's broader row-visibility
    /// gate.</summary>
    private async Task<Result<ActualWork>> AuthorizeAndLoadDraftAsync(Guid actualWorkId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWork>.Failure(gate.Error);

        var actualWork = await actualWorkPersistence.GetByIdAsync(currentUser.AccountId, actualWorkId, ct);
        if (actualWork is null)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotFound);

        var isResponsible = await responsibleCheck.IsActiveResponsibleAsync(
            actualWork.RequestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (!isResponsible)
            return Result<ActualWork>.Failure(KeepRequestErrors.NotFound);

        if (actualWork.Status != ActualWorkStatus.Draft)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotDraft);

        return Result<ActualWork>.Success(actualWork);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success — Blocked-only
    /// denies (read-only gate), not the mutation Blocked||ReadOnly posture, matching
    /// <see cref="ScopeNudgeFieldReadApiService.AuthorizeAsync"/>. Gate 3 checks
    /// <c>ActualWorkCapture</c>, matching <see cref="ActualWorkDraftApiService"/>.</summary>
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

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ActualWorkCapture))
        {
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);
        }

        var scope = roleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<KeepRequestVisibilityScope>.Success(scope);
    }
}
