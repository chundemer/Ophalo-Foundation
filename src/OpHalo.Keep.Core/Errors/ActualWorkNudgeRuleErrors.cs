using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ActualWorkNudgeRuleErrors
{
    public static readonly Error TriggerTargetRequired =
        Error.Create("ActualWorkNudgeRule.TriggerTargetRequired", "A catalog item or offering/assembly trigger target is required.");

    public static readonly Error TriggerTargetMustBeExclusive =
        Error.Create("ActualWorkNudgeRule.TriggerTargetMustBeExclusive", "An Actual Work nudge rule must trigger from exactly one catalog item or offering/assembly, not both.");

    public static readonly Error SuggestionOrderOutOfRange =
        Error.Create("ActualWorkNudgeRule.SuggestionOrderOutOfRange", "Suggestion order must be between 1 and 3.");

    public static readonly Error SuggestionCountOutOfRange =
        Error.Create("ActualWorkNudgeRule.SuggestionCountOutOfRange", "An Actual Work nudge rule must have between 1 and 3 suggestions.");

    public static readonly Error DuplicateSuggestionOrder =
        Error.Create("ActualWorkNudgeRule.DuplicateSuggestionOrder", "Each suggestion must have a distinct order.");

    public static readonly Error DuplicateSuggestionTarget =
        Error.Create("ActualWorkNudgeRule.DuplicateSuggestionTarget", "Each catalog item or offering/assembly may be suggested at most once per rule.");

    public static readonly Error SuggestionTargetRequired =
        Error.Create("ActualWorkNudgeRule.SuggestionTargetRequired", "A suggested catalog item or offering/assembly target is required.");

    public static readonly Error SuggestionTargetMustBeExclusive =
        Error.Create("ActualWorkNudgeRule.SuggestionTargetMustBeExclusive", "A suggestion must target exactly one catalog item or offering/assembly, not both.");

    public static readonly Error DuplicateTrigger =
        Error.Create("ActualWorkNudgeRule.DuplicateTrigger", "An account may configure at most one rule per trigger catalog item or offering/assembly.");

    public static readonly Error NotFound =
        Error.Create("ActualWorkNudgeRule.NotFound", "Actual Work nudge rule not found.");

    public static readonly Error TargetNotFound =
        Error.Create("ActualWorkNudgeRule.TargetNotFound", "The selected catalog item or offering/assembly could not be found.");

    public static readonly Error TriggerQueryParameterInvalid =
        Error.Create("ActualWorkNudgeRule.TriggerQueryParameterInvalid", "Exactly one of triggerCatalogItemId or triggerOfferingAssemblyId is required.");
}
