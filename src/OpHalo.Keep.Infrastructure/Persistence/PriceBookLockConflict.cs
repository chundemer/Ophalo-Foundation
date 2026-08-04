using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Shared exception-walk for the ADR-470 account-scoped publish-lock race, used by every
/// persistence class that shares the account's <c>PriceBookAccountState</c> lock and
/// <c>PriceBookVersion.VersionNumber</c> sequence (build-log/111, build-log/113 2e.2): a later
/// price publish and the atomic Save &amp; activate creation path alike. Walks the exception chain
/// for the account lock's concurrency-token mismatch (<see cref="DbUpdateConcurrencyException"/>),
/// the narrower race where two concurrent first-ever writes for the same account both try to
/// lazily create the lock row (unique violation), or a Serializable-isolation conflict (Postgres
/// SqlState 40001). A single filtered catch is needed rather than one per exception type because
/// EF Core's execution strategy re-wraps a transient-shaped DbUpdateException in an
/// InvalidOperationException rather than letting it surface directly — confirmed via the
/// concurrent-publish integration test.
/// </summary>
internal static class PriceBookLockConflict
{
    public static bool IsLockConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
                return true;
            if (current is PostgresException pg &&
                (pg.SqlState == PostgresErrorCodes.UniqueViolation || pg.SqlState == PostgresErrorCodes.SerializationFailure))
                return true;
        }

        return false;
    }
}
