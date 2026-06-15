using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestEventConfiguration : BaseEntityConfiguration<KeepRequestEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequestEvent> builder)
    {
        builder.ToTable("keep_request_events");

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasMaxLength(4000);

        builder.Property(x => x.ActorAccountUserId);

        builder.Property(x => x.Visibility)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.RequestId)
            .HasDatabaseName("ix_keep_request_events_request_id");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_keep_request_events_account_id");
    }
}
