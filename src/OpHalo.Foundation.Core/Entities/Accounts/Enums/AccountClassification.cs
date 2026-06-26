namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Operational and safety posture of an account. Separate from commercial lifecycle
/// (ADR-363). Production and Pilot are delivery-eligible; Demo and InternalTest are not.
/// </summary>
public enum AccountClassification
{
    Production = 1,
    Pilot = 2,
    Demo = 3,
    InternalTest = 4
}
