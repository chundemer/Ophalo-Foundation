using System.Net;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Shared branded layout for OpHalo-authored account emails (account start, sign-in, invite;
/// GAP-039 session 0.3; ADR-431 locked motto; ADR-446 transactional-email identity requirement).
/// Table-based HTML for email-client compatibility. No tracking pixel, no click tracking.
/// Customer-facing messages a business sends to its own customers are out of scope and must stay
/// business-primary with OpHalo only as a quiet footer mention — do not reuse this layout there.
/// </summary>
internal static class AccountEmailLayout
{
    private const string LogoUrl = "https://www.ophalo.com/brand/ophalo-lockup-color.png";
    private const string Motto = "The trust and continuity layer between businesses and customers.";
    private const string PrivacyUrl = "https://www.ophalo.com/privacy";
    private const string TermsUrl = "https://www.ophalo.com/terms";
    private const string ContactEmail = "pilot@ophalo.com";

    public static string BuildHtml(string introHtml, string ctaLabel, string ctaUrl, string footnoteHtml)
    {
        var encodedCtaUrl = WebUtility.HtmlEncode(ctaUrl);
        var encodedCtaLabel = WebUtility.HtmlEncode(ctaLabel);
        var encodedMotto = WebUtility.HtmlEncode(Motto);

        return $"""
            <!DOCTYPE html>
            <html>
              <body style="margin:0;padding:0;background-color:#f4f4f4;font-family:Arial,Helvetica,sans-serif;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f4;padding:24px 0;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:8px;overflow:hidden;">
                        <tr>
                          <td style="padding:32px 32px 16px 32px;text-align:center;">
                            <img src="{LogoUrl}" width="160" alt="OpHalo" style="display:block;margin:0 auto;max-width:160px;height:auto;" />
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:0 32px;color:#333333;font-size:15px;line-height:1.5;">
                            {introHtml}
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:24px 32px;text-align:center;">
                            <a href="{encodedCtaUrl}" style="display:inline-block;background-color:#bf6b43;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 24px;border-radius:6px;">{encodedCtaLabel}</a>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:0 32px 24px 32px;color:#666666;font-size:13px;line-height:1.5;">
                            {footnoteHtml}
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:16px 32px;border-top:1px solid #e5e5e5;text-align:center;color:#999999;font-size:12px;line-height:1.6;">
                            <div style="font-style:italic;margin-bottom:8px;">{encodedMotto}</div>
                            <div>
                              <a href="{PrivacyUrl}" style="color:#999999;">Privacy</a>
                              &nbsp;&middot;&nbsp;
                              <a href="{TermsUrl}" style="color:#999999;">Terms</a>
                              &nbsp;&middot;&nbsp;
                              <a href="mailto:{ContactEmail}" style="color:#999999;">Contact</a>
                            </div>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    public static string BuildText(string introText, string ctaLabel, string ctaUrl, string footnoteText) =>
        $"""
        ophalo

        {introText}

        {ctaLabel}: {ctaUrl}

        {footnoteText}

        ---
        {Motto}
        Privacy: {PrivacyUrl}
        Terms: {TermsUrl}
        Contact: {ContactEmail}
        """;
}

/// <summary>
/// Builds magic link email content for the existing-member sign-in and new-account flows.
/// Subject/HTML/text only — no vendor coupling.
/// </summary>
internal static class MagicLinkEmailTemplate
{
    public const string Subject = "Your OpHalo sign-in link";
    public const string NewAccountSubject = "Finish setting up your OpHalo account";

    private const string SignInIntro =
        "<p>You requested a link to sign in to your OpHalo account. This link expires in 24 hours.</p>";
    private const string SignInIntroText =
        "You requested a link to sign in to your OpHalo account. This link expires in 24 hours.";
    private const string NewAccountIntro =
        "<p>You're almost set up. Use the button below to finish creating your OpHalo account. This link expires in 24 hours.</p>";
    private const string NewAccountIntroText =
        "You're almost set up. Use the button below to finish creating your OpHalo account. This link expires in 24 hours.";
    private const string FootnoteHtml =
        "<p>If you did not request this link, you can safely ignore this email.</p>";
    private const string FootnoteText =
        "If you did not request this link, you can safely ignore this email.";

    public static string BuildHtmlBody(string magicLink) =>
        AccountEmailLayout.BuildHtml(SignInIntro, "Sign in to OpHalo", magicLink, FootnoteHtml);

    public static string BuildTextBody(string magicLink) =>
        AccountEmailLayout.BuildText(SignInIntroText, "Sign in to OpHalo", magicLink, FootnoteText);

    public static string BuildNewAccountHtmlBody(string magicLink) =>
        AccountEmailLayout.BuildHtml(NewAccountIntro, "Finish setting up your account", magicLink, FootnoteHtml);

    public static string BuildNewAccountTextBody(string magicLink) =>
        AccountEmailLayout.BuildText(NewAccountIntroText, "Finish setting up your account", magicLink, FootnoteText);
}
