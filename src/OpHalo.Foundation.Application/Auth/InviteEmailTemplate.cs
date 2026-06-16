using System.Net;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Builds invite email content. Subject and HTML body only — no vendor coupling.
/// </summary>
internal static class InviteEmailTemplate
{
    public static string BuildSubject(string businessName) =>
        $"You've been invited to join {WebUtility.HtmlEncode(businessName)} on OpHalo";

    public static string BuildHtmlBody(string businessName, string inviteLink) =>
        $"<p>You've been invited to join <strong>{WebUtility.HtmlEncode(businessName)}</strong> on OpHalo.</p>" +
        $"<p><a href=\"{WebUtility.HtmlEncode(inviteLink)}\">Accept your invitation</a></p>" +
        $"<p>This invitation expires in 7 days. If you did not expect this email, you can safely ignore it.</p>";
}
