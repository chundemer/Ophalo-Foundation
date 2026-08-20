using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One proposed suggestion in an Owner/Admin's Create/Update write. Exactly one of
/// <see cref="CatalogItemId"/>/<see cref="OfferingAssemblyId"/> is expected — the same exclusivity
/// <see cref="ActualWorkNudgeSuggestion"/> enforces in-domain.</summary>
public sealed record ActualWorkNudgeSuggestionApiCommand(Guid? CatalogItemId, Guid? OfferingAssemblyId);

public sealed record CreateActualWorkNudgeRuleApiCommand(
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ActualWorkNudgeSuggestionApiCommand> Suggestions);

public sealed record UpdateActualWorkNudgeRuleApiCommand(
    Guid RuleId,
    IReadOnlyList<ActualWorkNudgeSuggestionApiCommand> Suggestions);

/// <summary>One configured suggestion as seen by Owner/Admin configuration — always includes
/// ineligible/inactive targets (build-log/129), marked for repair rather than omitted.</summary>
public sealed record ActualWorkNudgeSuggestionConfigRow(
    Guid Id,
    int Order,
    Guid? SuggestedCatalogItemId,
    Guid? SuggestedOfferingAssemblyId,
    string TargetDisplayName,
    bool IsEligible);

public sealed record ActualWorkNudgeRuleConfigRow(
    Guid Id,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    string TriggerDisplayName,
    bool TriggerIsEligible,
    IReadOnlyList<ActualWorkNudgeSuggestionConfigRow> Suggestions);

/// <summary>
/// API-facing orchestration for the Owner/Admin Actual Work nudge rule-configuration surface
/// (build-log/129, 5d-ii-b): per-rule Create/Update/Delete plus an account-wide list, price-free.
/// Same gate composition and permission as <see cref="ScopeNudgeRuleConfigApiService"/> —
/// <c>PriceBookCatalogManage</c> — since this is catalog configuration, not a field/technician
/// action. Mutation is per-rule CRUD, not whole-set replace
/// (<see cref="IActualWorkNudgeRulePersistence"/>): <see cref="UpdateAsync"/> replaces only the
/// suggestion list of an existing rule and accepts no trigger fields.
///
/// Write-time target checks are existence-only (<see cref="CheckTargetExistsAsync"/>) — an
/// inactive/ineligible catalog item or assembly may be configured as a trigger or suggestion,
/// matching <see cref="ScopeNudgeRuleConfigApiService"/>'s explicit contrast with
/// <c>QuickScopeAction</c>'s write-time eligibility gate. Read rows still compute and expose current
/// eligibility so Owner/Admin can identify targets needing repair.
/// </summary>
public sealed class ActualWorkNudgeRuleConfigApiService(
    IActualWorkNudgeRulePersistence persistence,
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

    public async Task<Result<IReadOnlyList<ActualWorkNudgeRuleConfigRow>>> ListAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: false, ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<ActualWorkNudgeRuleConfigRow>>.Failure(gate.Error);

        var rules = await persistence.ListForAccountAsync(currentUser.AccountId, ct);
        var rows = new List<ActualWorkNudgeRuleConfigRow>(rules.Count);
        foreach (var rule in rules)
            rows.Add(await ToRowAsync(rule, ct));

        return Result<IReadOnlyList<ActualWorkNudgeRuleConfigRow>>.Success(rows);
    }

    public async Task<Result<ActualWorkNudgeRuleConfigRow>> CreateAsync(CreateActualWorkNudgeRuleApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: true, ct);
        if (gate.IsFailure)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(gate.Error);

        // Validate the aggregate shape (polymorphic exclusivity, suggestion count/set invariants)
        // before any lookup — a malformed request must fail deterministically on the domain error,
        // not on whichever target happens to be checked first.
        var created = ActualWorkNudgeRule.Create(
            currentUser.AccountId,
            command.TriggerCatalogItemId,
            command.TriggerOfferingAssemblyId,
            command.Suggestions.Select(s => (s.CatalogItemId, s.OfferingAssemblyId)).ToList(),
            currentUser.UserId);
        if (created.IsFailure)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(created.Error);

        if (created.Value.TriggerCatalogItemId is { } triggerCatalogItemId)
        {
            var exists = await CheckTargetExistsAsync(triggerCatalogItemId, null, ct);
            if (exists.IsFailure)
                return Result<ActualWorkNudgeRuleConfigRow>.Failure(exists.Error);
        }
        else if (created.Value.TriggerOfferingAssemblyId is { } triggerOfferingAssemblyId)
        {
            var exists = await CheckTargetExistsAsync(null, triggerOfferingAssemblyId, ct);
            if (exists.IsFailure)
                return Result<ActualWorkNudgeRuleConfigRow>.Failure(exists.Error);
        }

        foreach (var suggestion in created.Value.Suggestions)
        {
            var exists = await CheckTargetExistsAsync(suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId, ct);
            if (exists.IsFailure)
                return Result<ActualWorkNudgeRuleConfigRow>.Failure(exists.Error);
        }

        var commitResult = await persistence.CreateAsync(created.Value, ct);
        if (commitResult != ActualWorkNudgeRuleCommitResult.Committed)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(ActualWorkNudgeRuleErrors.DuplicateTrigger);

        var row = await ToRowAsync(created.Value, ct);
        return Result<ActualWorkNudgeRuleConfigRow>.Success(row);
    }

    public async Task<Result<ActualWorkNudgeRuleConfigRow>> UpdateAsync(UpdateActualWorkNudgeRuleApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: true, ct);
        if (gate.IsFailure)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(gate.Error);

        var rule = await persistence.GetByIdAsync(currentUser.AccountId, command.RuleId, ct);
        if (rule is null)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(ActualWorkNudgeRuleErrors.NotFound);

        // Validate the proposed suggestion set's shape before any lookup — same ordering
        // principle as CreateAsync.
        var replaceResult = rule.ReplaceSuggestions(
            command.Suggestions.Select(s => (s.CatalogItemId, s.OfferingAssemblyId)).ToList(),
            currentUser.UserId);
        if (replaceResult.IsFailure)
            return Result<ActualWorkNudgeRuleConfigRow>.Failure(replaceResult.Error);

        foreach (var suggestion in rule.Suggestions)
        {
            var exists = await CheckTargetExistsAsync(suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId, ct);
            if (exists.IsFailure)
                return Result<ActualWorkNudgeRuleConfigRow>.Failure(exists.Error);
        }

        await persistence.SaveAsync(rule, ct);

        var row = await ToRowAsync(rule, ct);
        return Result<ActualWorkNudgeRuleConfigRow>.Success(row);
    }

    public async Task<Result> DeleteAsync(Guid ruleId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: true, ct);
        if (gate.IsFailure)
            return gate;

        var rule = await persistence.GetByIdAsync(currentUser.AccountId, ruleId, ct);
        if (rule is null)
            return Result.Failure(ActualWorkNudgeRuleErrors.NotFound);

        await persistence.DeleteAsync(currentUser.AccountId, ruleId, ct);
        return Result.Success();
    }

    /// <summary>Existence-only check (build-log/129): a nudge trigger or suggestion may target an
    /// inactive/ineligible catalog item or assembly. Contrast with
    /// <see cref="QuickScopeActionConfigApiService"/>'s write-time eligibility gate.</summary>
    private async Task<Result> CheckTargetExistsAsync(Guid? catalogItemId, Guid? offeringAssemblyId, CancellationToken ct)
    {
        if (catalogItemId is { } id)
        {
            var detail = await catalogReadPersistence.GetItemDetailAsync(currentUser.AccountId, id, ct);
            return detail is null ? Result.Failure(ActualWorkNudgeRuleErrors.TargetNotFound) : Result.Success();
        }

        if (offeringAssemblyId is { } assemblyId)
        {
            var detail = await offeringAssemblyPersistence.GetDetailAsync(currentUser.AccountId, assemblyId, ct);
            return detail is null ? Result.Failure(ActualWorkNudgeRuleErrors.TargetNotFound) : Result.Success();
        }

        return Result.Success();
    }

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

    private async Task<ActualWorkNudgeRuleConfigRow> ToRowAsync(ActualWorkNudgeRule rule, CancellationToken ct)
    {
        var (triggerDisplayName, triggerIsEligible) = await ResolveTargetAsync(
            rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId, ct);

        var suggestionRows = new List<ActualWorkNudgeSuggestionConfigRow>(rule.Suggestions.Count);
        foreach (var suggestion in rule.Suggestions.OrderBy(s => s.Order))
        {
            var (displayName, isEligible) = await ResolveTargetAsync(
                suggestion.SuggestedCatalogItemId, suggestion.SuggestedOfferingAssemblyId, ct);
            suggestionRows.Add(new ActualWorkNudgeSuggestionConfigRow(
                suggestion.Id, suggestion.Order, suggestion.SuggestedCatalogItemId,
                suggestion.SuggestedOfferingAssemblyId, displayName, isEligible));
        }

        return new ActualWorkNudgeRuleConfigRow(
            rule.Id, rule.TriggerCatalogItemId, rule.TriggerOfferingAssemblyId,
            triggerDisplayName, triggerIsEligible, suggestionRows);
    }

    private async Task<Result> AuthorizeAsync(bool mutation, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. Mutation denies Blocked and ReadOnly (e.g. OffSeason); read
        // denies Blocked only, matching CatalogReadApiService vs CatalogCategoryApiService.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

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
        if (decision.IsBlocked || (mutation && decision.IsReadOnly))
            return Result.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — PriceBookCatalogManage (build-log/129, 5d-ii preflight): configuration follows
        // the catalog maintenance permission, not the field ActualWorkCapture permission.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role,
                roleSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.PriceBookCatalogManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
