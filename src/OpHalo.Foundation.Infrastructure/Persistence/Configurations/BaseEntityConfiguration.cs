using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared mapping for every <see cref="BaseEntity"/>: identity (domain-minted v7 GUID,
/// never DB-generated), audit timestamps, and soft-delete bookkeeping. Ported from the
/// reference app. Subtypes add their own mapping via <see cref="ConfigureEntity"/>.
/// </summary>
internal abstract class BaseEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : BaseEntity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .ValueGeneratedNever();

        builder.Property(entity => entity.CreatedAtUtc)
            .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
            .IsRequired();

        builder.Property(entity => entity.CreatedByUserId);
        builder.Property(entity => entity.ModifiedByUserId);
        builder.Property(entity => entity.DeletedAtUtc);
        builder.Property(entity => entity.DeletedByUserId);

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}
