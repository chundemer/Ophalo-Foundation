using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Users;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for PostAuthContinuation (ADR-497).
///
/// Does not extend BaseEntity — it has its own lifecycle fields, is never soft-deleted, and does
/// not participate in the SaveChangesAsync timestamp interception. The soft-delete global query
/// filter therefore does not apply to this table.
/// </summary>
internal sealed class PostAuthContinuationConfiguration : IEntityTypeConfiguration<PostAuthContinuation>
{
    public void Configure(EntityTypeBuilder<PostAuthContinuation> builder)
    {
        builder.ToTable("post_auth_continuations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.TargetAccountUserId);

        builder.Property(x => x.ClientType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DeviceName)
            .HasMaxLength(200);

        builder.Property(x => x.IssuedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.ConsumedAtUtc);

        // Derived — not persisted.
        builder.Ignore(x => x.IsConsumed);

        // Lookup at /auth/continue — must be unique; redemption always goes through hash.
        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_post_auth_continuations_token_hash");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_post_auth_continuations_user_id");

        // Bounded opportunistic cleanup on create.
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_post_auth_continuations_expires_at_utc");

        // Ephemeral, user-scoped auth artifact — cascade like AccountSession, not restrict like
        // the durable AccountUser -> User membership relationship.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
