namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Outcome of an outbound phone call. Valid only when ExternalContactDirection=Outbound
/// and CommunicationChannel=Phone.
/// </summary>
public enum ExternalContactOutcome
{
    SpokeWithCustomer = 1,
    LeftVoicemail = 2,
    NoAnswer = 3,
    WrongNumber = 4
}
