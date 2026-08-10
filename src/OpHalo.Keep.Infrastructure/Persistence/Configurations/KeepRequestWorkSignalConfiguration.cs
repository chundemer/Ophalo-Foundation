using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestWorkSignalConfiguration : BaseEntityConfiguration<KeepRequestWorkSignal>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequestWorkSignal> builder)
    {
        builder.ToTable("keep_request_work_signals");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.KeepRequestId)
            .IsRequired();

        builder.Property(x => x.SourceModuleKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.SignalKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.RaisedAtUtc)
            .IsRequired();

        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // ADR-463: unique on the full logical key — a later submission reopens this same row
        // rather than creating a second one. Plain (non-partial) unique index, so it is also a
        // valid ON CONFLICT target for the native upsert Session 3.3a.2's atomic submit operation
        // uses to raise/reopen without an application-level retry loop.
        builder.HasIndex(x => new { x.AccountId, x.KeepRequestId, x.SourceModuleKey, x.SignalKey })
            .IsUnique()
            .HasDatabaseName("ux_keep_request_work_signals_account_request_module_signal");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to KeepRequest(AccountId, Id) — prevents a signal referencing a request
        // from a different account; account-safe at the database level.
        builder.HasOne<KeepRequest>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.KeepRequestId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
