using System.Net;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Builds invite email content. Subject/HTML/text only — no vendor coupling.
/// </summary>
internal static class InviteEmailTemplate
{
    private const string FootnoteHtml =
        "<p>If you did not expect this email, you can safely ignore it.</p>";
    private const string FootnoteText =
        "If you did not expect this email, you can safely ignore it.";

    public static string BuildSubject(string businessName) =>
        $"You've been invited to join {WebUtility.HtmlEncode(businessName)} on OpHalo";

    public static string BuildHtmlBody(string businessName, string inviteLink)
    {
        var introHtml =
            $"<p>You've been invited to join <strong>{WebUtility.HtmlEncode(businessName)}</strong> on OpHalo. This invitation expires in 7 days.</p>";
        return AccountEmailLayout.BuildHtml(introHtml, "Accept your invitation", inviteLink, FootnoteHtml);
    }

    public static string BuildTextBody(string businessName, string inviteLink)
    {
        var introText =
            $"You've been invited to join {businessName} on OpHalo. This invitation expires in 7 days.";
        return AccountEmailLayout.BuildText(introText, "Accept your invitation", inviteLink, FootnoteText);
    }
}
