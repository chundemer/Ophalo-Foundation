using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Distinct commit outcomes for <see cref="IScopeNudgeRulePersistence.CreateAsync"/>
/// (build-log/123). The database is the actual guard — the in-domain
/// <see cref="ScopeNudgeRule"/>/<see cref="ScopeNudgeSuggestionSetValidator"/> checks a caller runs
/// first narrow the common case, but a race between two concurrent Owner/Admin writes must still be
/// caught here rather than surfacing as an unhandled 500.
/// </summary>
public enum ScopeNudgeRuleCommitResult
{
    Committed,

    /// <summary>An account already has a rule for this trigger — one of the two partial unique
    /// indexes on <c>(AccountId, TriggerCatalogItemId)</c> / <c>(AccountId,
    /// TriggerOfferingAssemblyId)</c>.</summary>
    DuplicateTrigger,
}

/// <summary>
/// Persistence seam for <see cref="ScopeNudgeRule"/>, per-rule CRUD rather than the whole-set
/// replace pattern used by <c>QuickScopeAction</c> (build-log/123). Every read/write is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row. No API layer consumes this yet (Session 1 is
/// persistence/domain-only) — the Session 2 configuration service is the first caller.
/// </summary>
public interface IScopeNudgeRulePersistence
{
    /// <summary>Loads a single rule with its suggestions attached and tracked, or null if no rule
    /// with that id exists for the account. Callers mutate the returned aggregate (e.g. via
    /// <see cref="ScopeNudgeRule.ReplaceSuggestions"/>) and persist it with
    /// <see cref="SaveAsync"/>.</summary>
    Task<ScopeNudgeRule?> GetByIdAsync(Guid accountId, Guid ruleId, CancellationToken ct);

    /// <summary>Returns every rule configured for the account, including rules whose trigger or
    /// suggestion targets have since become inactive/ineligible — ordered by <c>CreatedAtUtc</c>.
    /// Empty for an account with no configuration — never null.</summary>
    Task<IReadOnlyList<ScopeNudgeRule>> ListForAccountAsync(Guid accountId, CancellationToken ct);

    /// <summary>Loads the single rule (with suggestions attached, untracked) whose trigger matches
    /// exactly one of <paramref name="triggerCatalogItemId"/> / <paramref name="triggerOfferingAssemblyId"/>,
    /// or null if no rule is configured for that trigger. The field nudge-read path (build-log/123)
    /// is the sole caller — callers must supply exactly one non-null id.</summary>
    Task<ScopeNudgeRule?> GetByTriggerAsync(
        Guid accountId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId, CancellationToken ct);

    /// <summary>Inserts a newly created rule and its initial suggestion set.</summary>
    Task<ScopeNudgeRuleCommitResult> CreateAsync(ScopeNudgeRule rule, CancellationToken ct);

    /// <summary>Persists changes to a tracked aggregate previously returned by
    /// <see cref="GetByIdAsync"/> — e.g. a suggestion-list replace. Trigger fields are immutable and
    /// never change through this path.</summary>
    Task SaveAsync(ScopeNudgeRule rule, CancellationToken ct);

    /// <summary>Removes the rule and its suggestion rows (cascade). No-op if the id does not exist
    /// for the account.</summary>
    Task DeleteAsync(Guid accountId, Guid ruleId, CancellationToken ct);
}
