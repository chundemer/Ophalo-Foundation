using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Users;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : BaseEntityConfiguration<User>
{
    protected override void ConfigureEntity(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Phone)
            .HasMaxLength(50);

        builder.Property(x => x.IsEmailVerified)
            .IsRequired();

        builder.Property(x => x.EmailVerifiedAtUtc);

        builder.Property(x => x.LastLoginAtUtc);

        // Primary lookup — email must be unique across active (non-deleted) users.
        builder.HasIndex(x => x.Email)
            .HasFilter("deleted_at_utc IS NULL")
            .IsUnique();
    }
}
