namespace OpHalo.Api.Keep;

public sealed record UpdatePolicyBody(
    int FirstResponseTargetMinutes,
    int StandardResponseTargetMinutes,
    int PriorityResponseTargetMinutes,
    int StatusCheckThresholdDays);
