using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

public sealed class RedeemMobileHandoffService(
    IMobileHandoffCodePersistence persistence,
    IAccountSessionService sessionService,
    IClock clock,
    ILogger<RedeemMobileHandoffService> logger)
{
    public async Task<Result<MobileHandoffRedeemResult>> HandleAsync(
        string rawCode,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode.Trim());

        var code = await persistence.FindByHashAsync(codeHash, cancellationToken);
        if (code is null || code.IsExpired(nowUtc) || code.IsConsumed)
            return Result<MobileHandoffRedeemResult>.Failure(MobileHandoffCodeErrors.InvalidToken);

        var consumed = await persistence.ConsumeAsync(code.Id, nowUtc, cancellationToken);
        if (!consumed)
            return Result<MobileHandoffRedeemResult>.Failure(MobileHandoffCodeErrors.InvalidToken);

        try
        {
            var session = await sessionService.CreateSession(
                code.AccountId,
                code.AccountUserId,
                SessionClientType.MobileApp,
                deviceName,
                cancellationToken);

            return Result<MobileHandoffRedeemResult>.Success(
                new MobileHandoffRedeemResult(session.RawToken, session.ExpiresAtUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Session creation failed after mobile handoff redemption. AccountId={AccountId} AccountUserId={AccountUserId} MobileHandoffCodeId={MobileHandoffCodeId}",
                code.AccountId, code.AccountUserId, code.Id);

            return Result<MobileHandoffRedeemResult>.Failure(AccountErrors.SessionCreationFailed);
        }
    }
}

public sealed record MobileHandoffRedeemResult(string RawToken, DateTime ExpiresAtUtc);
