using System.Net.Http.Json;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Email;

/// <summary>
/// IEmailSender implementation backed by the Resend HTTP API.
/// Registered as a typed HttpClient — base address and Authorization header
/// are configured at startup so this class carries no API key knowledge.
/// </summary>
public sealed class ResendEmailSender(HttpClient httpClient, ResendSettings settings) : IEmailSender
{
    public async Task<Result> SendAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            from = settings.FromAddress,
            to = new[] { to },
            subject = subject,
            html = htmlBody
        };

        using var response = await httpClient.PostAsJsonAsync("/emails", payload, cancellationToken);

        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(Error.Create("Email.DeliveryFailed", "Email delivery failed."));
    }
}
