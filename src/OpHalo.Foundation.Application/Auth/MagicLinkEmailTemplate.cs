using System.Net;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Builds magic link email content. Subject and HTML body only — no vendor coupling.
/// </summary>
internal static class MagicLinkEmailTemplate
{
    public const string Subject = "Your OpHalo sign-in link";
    public const string NewAccountSubject = "Finish setting up your OpHalo account";

    public static string BuildHtmlBody(string magicLink) =>
        $"<p>Click the link below to sign in to OpHalo:</p>" +
        $"<p><a href=\"{WebUtility.HtmlEncode(magicLink)}\">Sign in to OpHalo</a></p>" +
        $"<p>This link expires in 24 hours. If you did not request this link, you can safely ignore this email.</p>";
}
