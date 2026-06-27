using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepProductOpsEventConfiguration : BaseEntityConfiguration<KeepProductOpsEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepProductOpsEvent> builder)
    {
        builder.ToTable("keep_product_ops_events");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        // Unique per account — enforces singleton signals at the DB level and prevents
        // duplicate inserts from concurrent requests. Recurring signal types are deferred
        // (WeeklyInactivity, NegativeFeedbackReceived); relax this constraint when added.
        builder.HasIndex(x => new { x.AccountId, x.EventType })
            .IsUnique()
            .HasDatabaseName("ix_keep_product_ops_events_account_event_type");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
