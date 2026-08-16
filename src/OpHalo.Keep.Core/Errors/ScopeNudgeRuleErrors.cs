using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ScopeNudgeRuleErrors
{
    public static readonly Error TriggerTargetRequired =
        Error.Create("ScopeNudgeRule.TriggerTargetRequired", "A catalog item or offering/assembly trigger target is required.");

    public static readonly Error TriggerTargetMustBeExclusive =
        Error.Create("ScopeNudgeRule.TriggerTargetMustBeExclusive", "A scope nudge rule must trigger from exactly one catalog item or offering/assembly, not both.");

    public static readonly Error SuggestionOrderOutOfRange =
        Error.Create("ScopeNudgeRule.SuggestionOrderOutOfRange", "Suggestion order must be between 1 and 3.");

    public static readonly Error SuggestionCountOutOfRange =
        Error.Create("ScopeNudgeRule.SuggestionCountOutOfRange", "A scope nudge rule must have between 1 and 3 suggestions.");

    public static readonly Error DuplicateSuggestionOrder =
        Error.Create("ScopeNudgeRule.DuplicateSuggestionOrder", "Each suggestion must have a distinct order.");

    public static readonly Error DuplicateSuggestionTarget =
        Error.Create("ScopeNudgeRule.DuplicateSuggestionTarget", "Each catalog item or offering/assembly may be suggested at most once per rule.");

    public static readonly Error SuggestionTargetRequired =
        Error.Create("ScopeNudgeRule.SuggestionTargetRequired", "A suggested catalog item or offering/assembly target is required.");

    public static readonly Error SuggestionTargetMustBeExclusive =
        Error.Create("ScopeNudgeRule.SuggestionTargetMustBeExclusive", "A suggestion must target exactly one catalog item or offering/assembly, not both.");

    public static readonly Error DuplicateTrigger =
        Error.Create("ScopeNudgeRule.DuplicateTrigger", "An account may configure at most one rule per trigger catalog item or offering/assembly.");
}
