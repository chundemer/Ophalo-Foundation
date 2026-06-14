using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Abstractions.Messaging;

/// <summary>
/// Transport abstraction for outbound email delivery. Implementations are
/// infrastructure concerns — application code never references a vendor directly.
/// </summary>
/// <remarks>
/// Canonical, single definition. Collapsed in Phase 3 from the reference repo's
/// duplicate pair (OpHalo.Shared.Abstractions.IEmailSender and
/// OpHalo.Application.Abstractions.Infrastructure.IEmailSender). Email sending is a
/// Foundation concern, so it lives in Foundation.Application — the SharedKernel must
/// not contain email sending (build plan §3.3, §8).
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email to the specified recipient. Returns <see cref="Result.Failure"/>
    /// if delivery is rejected by the provider. Provider failures are expected
    /// operational conditions, not exceptions.
    /// </summary>
    Task<Result> SendAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken);
}
