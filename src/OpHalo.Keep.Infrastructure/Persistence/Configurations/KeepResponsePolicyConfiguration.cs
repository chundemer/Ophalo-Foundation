using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepResponsePolicyConfiguration : BaseEntityConfiguration<KeepResponsePolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepResponsePolicy> builder)
    {
        builder.ToTable("keep_response_policies");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.FirstResponseTargetMinutes)
            .IsRequired();

        builder.Property(x => x.StandardResponseTargetMinutes)
            .IsRequired();

        builder.Property(x => x.PriorityResponseTargetMinutes)
            .IsRequired();

        builder.Property(x => x.StatusCheckThresholdDays)
            .IsRequired();

        builder.Property(x => x.BusinessHoursOnly);

        // ADR-097: one policy per account.
        builder.HasIndex(x => x.AccountId)
            .IsUnique()
            .HasDatabaseName("ix_keep_response_policies_account_id");

        // FK — restricts deletion of an account that has a response policy.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
