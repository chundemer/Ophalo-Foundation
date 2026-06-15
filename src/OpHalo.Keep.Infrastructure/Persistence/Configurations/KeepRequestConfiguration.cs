using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestConfiguration : BaseEntityConfiguration<KeepRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequest> builder)
    {
        builder.ToTable("keep_requests");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.KeepCustomerId)
            .IsRequired();

        builder.Property(x => x.CustomerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.CustomerPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CustomerEmail)
            .HasMaxLength(320);

        builder.Property(x => x.Description)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.CurrentStatusText)
            .HasMaxLength(500);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ReferenceCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.PageToken)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc);
        builder.Property(x => x.ClosedAtUtc);
        builder.Property(x => x.LastBusinessActivityAt).IsRequired();
        builder.Property(x => x.LastCustomerActivityAt);

        // IsTerminal is a computed C# property — no column.
        builder.Ignore(x => x.IsTerminal);

        builder.HasIndex(x => x.PageToken)
            .IsUnique()
            .HasDatabaseName("ix_keep_requests_page_token");

        builder.HasIndex(x => new { x.AccountId, x.ReferenceCode })
            .IsUnique()
            .HasDatabaseName("ix_keep_requests_account_reference_code");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_keep_requests_account_id");
    }
}
