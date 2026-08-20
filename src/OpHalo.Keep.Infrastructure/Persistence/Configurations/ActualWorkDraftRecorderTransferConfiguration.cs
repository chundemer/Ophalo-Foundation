using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkDraftRecorderTransferConfiguration : BaseEntityConfiguration<ActualWorkDraftRecorderTransfer>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWorkDraftRecorderTransfer> builder)
    {
        builder.ToTable("keep_actual_work_draft_recorder_transfers");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ActualWorkId)
            .IsRequired();

        builder.Property(x => x.ActorAccountUserId)
            .IsRequired();

        builder.Property(x => x.PriorRecorderAccountUserId)
            .IsRequired();

        builder.Property(x => x.NewRecorderAccountUserId)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.TransferredAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.ActualWorkId });

        // Composite FK — prevents a transfer record referencing a Draft from a different account.
        // ClientCascade, matching ActualWorkLineConfiguration: Discard hard-deletes the ActualWork
        // row, and a transfer audit event only ever exists for a Draft (never a submitted, permanent
        // visit) — there is nothing left to audit once the Draft itself is gone.
        builder.HasOne<ActualWork>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActualWorkId })
            .HasPrincipalKey(w => new { w.AccountId, w.Id })
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
