using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepCalendarClosureConfiguration : BaseEntityConfiguration<KeepCalendarClosure>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepCalendarClosure> builder)
    {
        builder.ToTable("keep_calendar_closures");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ClosureDate)
            .IsRequired();

        builder.Property(x => x.Label)
            .HasMaxLength(200);

        // ADR-505: one closure row per account-local date.
        builder.HasIndex(x => new { x.AccountId, x.ClosureDate })
            .IsUnique()
            .HasDatabaseName("ix_keep_calendar_closures_account_id_closure_date");

        // FK — restricts deletion of an account that has a calendar closure.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
