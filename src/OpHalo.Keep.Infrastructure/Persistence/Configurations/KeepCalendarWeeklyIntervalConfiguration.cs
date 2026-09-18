using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepCalendarWeeklyIntervalConfiguration : BaseEntityConfiguration<KeepCalendarWeeklyInterval>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepCalendarWeeklyInterval> builder)
    {
        builder.ToTable("keep_calendar_weekly_intervals");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Weekday)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.OpensAt)
            .IsRequired();

        builder.Property(x => x.ClosesAt)
            .IsRequired();

        // ADR-505: at most one open interval per weekday.
        builder.HasIndex(x => new { x.AccountId, x.Weekday })
            .IsUnique()
            .HasDatabaseName("ix_keep_calendar_weekly_intervals_account_id_weekday");

        // FK — restricts deletion of an account that has a calendar interval.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
