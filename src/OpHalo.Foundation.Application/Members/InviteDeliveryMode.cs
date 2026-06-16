namespace OpHalo.Foundation.Application.Members;

/// <summary>
/// Controls how an invite link is delivered to the invitee.
/// Service-level input — not a domain concept. Explicit 1-based values to prevent reorder drift.
/// </summary>
public enum InviteDeliveryMode
{
    /// <summary>Send the invite link by email. The URL is not returned in the API response.</summary>
    Email = 1,

    /// <summary>
    /// Return the invite URL in the API response for the caller to share via SMS, iMessage, etc.
    /// No email is sent. The URL is returned once to an authenticated Owner/Admin — it must not
    /// be logged anywhere in the request pipeline.
    /// </summary>
    ManualShare = 2
}
