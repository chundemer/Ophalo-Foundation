using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkConfiguration : BaseEntityConfiguration<ActualWork>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWork> builder)
    {
        builder.ToTable("keep_actual_works");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.CompletionNote)
            .HasMaxLength(1000);

        builder.Property(x => x.SubmittedAtUtc);

        // Application-managed opaque uuid token — same pattern as
        // ProposedScope.ConcurrencyVersion (ADR-330).
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // Pilot lock (build-log/129): at most one open Draft visit per request at a time.
        builder.HasIndex(x => x.RequestId)
            .IsUnique()
            .HasFilter("status = 'Draft'")
            .HasDatabaseName("ux_keep_actual_works_open_draft");

        builder.HasIndex(x => new { x.AccountId, x.RequestId });

        // Alternate key — supports the composite FK from ActualWorkLine.ActualWorkId, which must
        // reference (AccountId, Id) to prevent cross-account line assignment.
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_actual_works_account_id");

        // Lines is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API.
        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to KeepRequest(AccountId, Id) — prevents a visit referencing a request from
        // a different account; account-safe at the database level, no post-load tenant check.
        builder.HasOne<KeepRequest>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.RequestId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
