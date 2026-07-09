using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

public sealed class KeepSetupDeferral : BaseEntity
{
    public Guid AccountId { get; private set; }
    public KeepSetupStep Step { get; private set; }
    public DateTime DeferredAtUtc { get; private set; }
    public DateTime? ClearedAtUtc { get; private set; }
    public Guid DeferredByAccountUserId { get; private set; }

    public static KeepSetupDeferral Create(
        Guid accountId,
        KeepSetupStep step,
        DateTime deferredAtUtc,
        Guid deferredByAccountUserId)
    {
        return new KeepSetupDeferral
        {
            AccountId = accountId,
            Step = step,
            DeferredAtUtc = deferredAtUtc,
            DeferredByAccountUserId = deferredByAccountUserId
        };
    }

    public void Clear(DateTime clearedAtUtc) => ClearedAtUtc = clearedAtUtc;

    public void Redefer(DateTime deferredAtUtc, Guid deferredByAccountUserId)
    {
        DeferredAtUtc = deferredAtUtc;
        DeferredByAccountUserId = deferredByAccountUserId;
        ClearedAtUtc = null;
    }
}
