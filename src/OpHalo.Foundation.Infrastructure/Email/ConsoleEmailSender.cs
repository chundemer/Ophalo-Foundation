using System.Net;
using System.Text.RegularExpressions;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Email;

/// <summary>
/// Dev-only email sender. Used when Resend config is absent in Development.
/// Writes the magic-link URL plainly to console — not through structured logging
/// so codes never appear in log pipelines.
/// </summary>
public sealed partial class ConsoleEmailSender : IEmailSender
{
    [GeneratedRegex(@"href=""([^""]+)""")]
    private static partial Regex HrefPattern();

    public Task<Result> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken)
    {
        var match = HrefPattern().Match(htmlBody);
        var url = match.Success
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : "(URL not found in body)";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Error.WriteLine("──── [DEV EMAIL] ────────────────────────────────────────");
        Console.Error.WriteLine($"To:      {to}");
        Console.Error.WriteLine($"Subject: {subject}");
        Console.Error.WriteLine($"URL:     {url}");
        Console.Error.WriteLine("─────────────────────────────────────────────────────────");
        Console.ResetColor();

        return Task.FromResult(Result.Success());
    }
}
