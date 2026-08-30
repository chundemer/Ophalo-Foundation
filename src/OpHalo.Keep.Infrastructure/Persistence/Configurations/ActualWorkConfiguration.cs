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

        builder.Property(x => x.ReviewedAtUtc);

        builder.Property(x => x.ReviewedByAccountUserId);

        builder.Property(x => x.ReviewNote)
            .HasMaxLength(2000);

        // ADR-494 D5 (4c-ii): optional Draft-editable field note, frozen at submit. Nullable,
        // trimmed-to-null and length-bounded by the domain (ActualWork.SetVisitNote).
        builder.Property(x => x.VisitNote)
            .HasMaxLength(2000);

        // Current recorder-ownership holder (GAP-055) — distinct from the immutable
        // CreatedByUserId authorship column already mapped by BaseEntityConfiguration.
        builder.Property(x => x.RecorderAccountUserId)
            .IsRequired();

        // ADR-494 D2: optional Draft-level "Performed by" default that seeds new lines. Nullable, no
        // FK (an account-user id, same rationale as the line's PerformedByAccountUserId).
        builder.Property(x => x.DefaultPerformedByAccountUserId);

        // Account-scoped lookup of the visits carrying a given user as the ticket default —
        // supports the performer-candidate / "my open drafts" reads in later 4c-i slices.
        builder.HasIndex(x => new { x.AccountId, x.DefaultPerformedByAccountUserId });

        // ADR-494 D4/D6/D6b — supersession marker columns. Nullable on a live visit; all four are
        // set together by ActualWork.Supersede when an erroneous submitted visit is replaced.
        builder.Property(x => x.SupersededAtUtc);
        builder.Property(x => x.SupersededByAccountUserId);
        builder.Property(x => x.SupersessionReason)
            .HasMaxLength(2000);

        // Self-reference to the single direct successor, composite on (AccountId, Id) so a
        // supersession link can never cross accounts (same discipline as the KeepRequest FK below).
        builder.Property(x => x.SupersededByActualWorkId);
        builder.HasOne<ActualWork>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.SupersededByActualWorkId })
            .HasPrincipalKey(x => new { x.AccountId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // ADR-494 D6b — one-to-one supersession: a given successor supersedes exactly one source.
        builder.HasIndex(x => x.SupersededByActualWorkId)
            .IsUnique()
            .HasFilter("superseded_by_actual_work_id IS NOT NULL")
            .HasDatabaseName("ux_keep_actual_works_superseded_by");

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
