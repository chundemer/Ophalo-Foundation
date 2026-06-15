namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Config-backed defaults for new account creation at /auth/start.
/// Bound from the "SignupDefaults" section in appsettings.
///
/// IsPilot controls the pilot cohort flag on new AccountEntitlements.
/// TrialDurationDays sets the trial window length.
/// MaxPilotAccounts gates pilot capacity when IsPilot is true; null means no cap.
/// </summary>
public sealed class SignupDefaultsSettings
{
    public bool IsPilot { get; init; }
    public int TrialDurationDays { get; init; } = 30;
    public int? MaxPilotAccounts { get; init; }
}
