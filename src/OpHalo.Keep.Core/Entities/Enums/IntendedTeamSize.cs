namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Setup guidance only. Does not affect seat limits or AccountEntitlements.MaxUserSeats.
/// Stored in KeepAccountSetupPreferences (Keep layer), never on Foundation Account.
/// </summary>
public enum IntendedTeamSize
{
    JustMe = 1,
    TwoToFive = 2,
    SixPlus = 3
}
