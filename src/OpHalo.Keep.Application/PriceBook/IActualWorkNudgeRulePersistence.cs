using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Distinct commit outcomes for <see cref="IActualWorkNudgeRulePersistence.CreateAsync"/>
/// (5d-ii-a2). The database is the actual guard — the in-domain <see cref="ActualWorkNudgeRule"/>/
/// <see cref="ActualWorkNudgeSuggestionSetValidator"/> checks a caller runs first narrow the common
/// case, but a race between two concurrent Owner/Admin writes must still be caught here rather than
/// surfacing as an unhandled 500.</summary>
public enum ActualWorkNudgeRuleCommitResult
{
    Committed,

    /// <summary>An account already has a rule for this trigger — one of the two partial unique
    /// indexes on <c>(AccountId, TriggerCatalogItemId)</c> / <c>(AccountId,
    /// TriggerOfferingAssemblyId)</c>.</summary>
    DuplicateTrigger,
}

/// <summary>
/// Persistence seam for <see cref="ActualWorkNudgeRule"/>, per-rule CRUD — same shape as
/// <see cref="IScopeNudgeRulePersistence"/>, distinct table. Every read/write is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row.
/// </summary>
public interface IActualWorkNudgeRulePersistence
{
    /// <summary>Loads a single rule with its suggestions attached and tracked, or null if no rule
    /// with that id exists for the account. Callers mutate the returned aggregate (e.g. via
    /// <see cref="ActualWorkNudgeRule.ReplaceSuggestions"/>) and persist it with
    /// <see cref="SaveAsync"/>.</summary>
    Task<ActualWorkNudgeRule?> GetByIdAsync(Guid accountId, Guid ruleId, CancellationToken ct);

    /// <summary>Returns every rule configured for the account, including rules whose trigger or
    /// suggestion targets have since become inactive/ineligible — ordered by <c>CreatedAtUtc</c>.
    /// Empty for an account with no configuration — never null.</summary>
    Task<IReadOnlyList<ActualWorkNudgeRule>> ListForAccountAsync(Guid accountId, CancellationToken ct);

    /// <summary>Loads the single rule (with suggestions attached, untracked) whose trigger matches
    /// exactly one of <paramref name="triggerCatalogItemId"/> / <paramref name="triggerOfferingAssemblyId"/>,
    /// or null if no rule is configured for that trigger. The field nudge-read path (5d-ii-c) is the
    /// sole caller — callers must supply exactly one non-null id.</summary>
    Task<ActualWorkNudgeRule?> GetByTriggerAsync(
        Guid accountId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId, CancellationToken ct);

    /// <summary>Inserts a newly created rule and its initial suggestion set.</summary>
    Task<ActualWorkNudgeRuleCommitResult> CreateAsync(ActualWorkNudgeRule rule, CancellationToken ct);

    /// <summary>Persists changes to a tracked aggregate previously returned by
    /// <see cref="GetByIdAsync"/> — e.g. a suggestion-list replace. Trigger fields are immutable and
    /// never change through this path.</summary>
    Task SaveAsync(ActualWorkNudgeRule rule, CancellationToken ct);

    /// <summary>Removes the rule and its suggestion rows (cascade). No-op if the id does not exist
    /// for the account.</summary>
    Task DeleteAsync(Guid accountId, Guid ruleId, CancellationToken ct);
}
