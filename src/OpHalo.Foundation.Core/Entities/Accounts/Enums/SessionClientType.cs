namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Identifies the client surface that created the session (Decision 2, ADR-061).
/// Stored on AccountSession to support device-list display and future session-management controls.
/// </summary>
public enum SessionClientType
{
    /// <summary>Web browser session created via the ophalo.sid cookie transport.</summary>
    Browser = 1,

    /// <summary>Native mobile app session using opaque Bearer token transport.</summary>
    MobileApp = 2,

    /// <summary>Internal admin tooling session.</summary>
    Admin = 3
}
