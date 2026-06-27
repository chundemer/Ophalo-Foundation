namespace OpHalo.Keep.Application.Setup;

public sealed record KeepSetupResult(
    string BusinessName,
    string TimeZone,
    string? CustomerFacingPhone,
    string? CustomerFacingEmail,
    KeepSetupPolicyResult ResponsePolicy);

public sealed record KeepSetupPolicyResult(
    int FirstResponseTargetMinutes,
    int StandardResponseTargetMinutes,
    int PriorityResponseTargetMinutes,
    int StatusCheckThresholdDays);
