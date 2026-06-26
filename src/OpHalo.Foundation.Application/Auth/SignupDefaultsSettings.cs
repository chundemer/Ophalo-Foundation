using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Config-backed defaults for new account creation at /auth/start.
/// Bound from the "SignupDefaults" section in appsettings.
///
/// Classification sets the account classification for new signups (ADR-364).
/// Only Production and Pilot are valid for public signup.
/// MaxPilotAccounts gates pilot capacity when Classification is Pilot; null means no cap.
/// </summary>
public sealed class SignupDefaultsSettings
{
    public AccountClassification Classification { get; init; } = AccountClassification.Pilot;
    public int TrialDurationDays { get; init; } = 30;
    public int? MaxPilotAccounts { get; init; }
}
