namespace OpHalo.Foundation.Core.Entities.Shared;

/// <summary>
/// Base type for all persisted Foundation entities. Provides identity, audit
/// timestamps, and soft-delete bookkeeping. Ported verbatim from the reference app.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; init; }
    public Guid? ModifiedByUserId { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public bool IsDeleted => DeletedAtUtc.HasValue;
}
