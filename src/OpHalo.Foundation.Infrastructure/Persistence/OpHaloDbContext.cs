using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.SharedKernel.Abstractions;


namespace OpHalo.Foundation.Infrastructure.Persistence;

/// <summary>
/// The Foundation persistence context. Maps the trimmed Foundation aggregate
/// (<see cref="Account"/>, <see cref="AccountUser"/>, <see cref="User"/>,
/// <see cref="AccountEntitlements"/>) to PostgreSQL.
/// </summary>
/// <remarks>
/// Ported from the reference app's <c>ApplicationDbContext</c>, keeping two behaviors
/// verbatim — the <see cref="SaveChangesAsync"/> timestamp interception and the
/// soft-delete global query filters — while dropping the Keep/Platform/Continuity
/// DbSets and the legacy <c>SystemRecord</c> / non-<see cref="BaseEntity"/> branches,
/// none of which exist in this foundation.
/// </remarks>
/// <param name="additionalModelAssemblies">
/// Optional extra assemblies to scan for <see cref="IEntityTypeConfiguration{T}"/>
/// implementations. Pass the Keep.Infrastructure assembly here so EF discovers Keep
/// entity configs without Foundation.Infrastructure taking a compile-time dependency
/// on Keep (architecture boundary: Foundation must not reference Keep).
/// </param>
public sealed class OpHaloDbContext(
    DbContextOptions<OpHaloDbContext> options,
    IClock clock,
    IEnumerable<Assembly>? additionalModelAssemblies = null)
    : DbContext(options)
{
    private readonly IReadOnlyList<Assembly> _additionalAssemblies =
        additionalModelAssemblies?.ToList() ?? [];
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountUser> AccountUsers => Set<AccountUser>();
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();
    public DbSet<AccountAuthCode> AccountAuthCodes => Set<AccountAuthCode>();
    public DbSet<AccountUserDevice> AccountUserDevices => Set<AccountUserDevice>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AccountEntitlements> AccountEntitlements => Set<AccountEntitlements>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpHaloDbContext).Assembly);

        foreach (var assembly in _additionalAssemblies)
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);

        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(BaseEntity.CreatedAtUtc)).CurrentValue = now;
                    entry.Property(nameof(BaseEntity.UpdatedAtUtc)).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(BaseEntity.UpdatedAtUtc)).CurrentValue = now;
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // Foundational satellites are excluded from the soft-delete filter (ADR-025).
            // These rows must always be visible — they are required invariants, not deletable data.
            if (IsFoundationalSatellite(entityType.ClrType))
                continue;

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildSoftDeleteFilter(entityType.ClrType));
        }
    }

    private static bool IsFoundationalSatellite(Type entityType) =>
        entityType == typeof(AccountEntitlements);

    private static LambdaExpression BuildSoftDeleteFilter(Type entityClrType)
    {
        var parameter = Expression.Parameter(entityClrType, "entity");
        var deletedAtProperty = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(DateTime?)],
            parameter,
            Expression.Constant(nameof(BaseEntity.DeletedAtUtc)));

        var body = Expression.Equal(
            deletedAtProperty,
            Expression.Constant(null, typeof(DateTime?)));

        return Expression.Lambda(body, parameter);
    }
}
