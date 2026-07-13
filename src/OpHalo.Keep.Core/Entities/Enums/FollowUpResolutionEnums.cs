namespace OpHalo.Keep.Core.Entities.Enums;

public enum FollowUpResolutionOutcome
{
    Complete   = 1,
    Move       = 2,
    KeepActive = 3
}

public enum FollowUpCompletionReason
{
    CustomerContacted = 1,
    WorkCompleted     = 2,
    NoLongerNeeded    = 3,
    Other             = 4
}
