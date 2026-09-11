using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Foundation.Infrastructure.Auth;

/// <summary>
/// PostgreSQL-backed <see cref="IAuthIssuanceThrottle"/> (BL154). Deliberately not modeled as an
/// EF entity/DbSet — the (scope, key_hash) row is pure counter state, not a domain concept, and a
/// single atomic <c>INSERT … ON CONFLICT … DO UPDATE … WHERE … RETURNING</c> statement per
/// partition (not EF read-then-write) is what makes each increment race-safe under concurrent API
/// instances (Vector 11). <see cref="DbContext.Database"/>'s scalar <c>SqlQuery</c> reads the
/// statement's <c>RETURNING</c> row without requiring model registration — the result column must
/// be aliased "Value", the contract EF's scalar SqlQuery binds to.
///
/// Requires the `auth_issuance_throttles` table (scope text, key_hash text, window_start_utc
/// timestamptz, count int, PRIMARY KEY (scope, key_hash), plus an index on window_start_utc for
/// the prune below) — see BL154 for the exact DDL Christian generates.
/// </summary>
public sealed class EfAuthIssuanceThrottle(OpHaloDbContext db, IClock clock, ILogger<EfAuthIssuanceThrottle> logger)
    : IAuthIssuanceThrottle
{
    // Bounded per-acquisition prune (mirrors EfFeedbackPersistence's retention-sweep batch size):
    // keeps the table from growing unboundedly with abandoned/one-shot keys without a separate
    // background job. Any window older than this is expired for every current scope (15m/60m),
    // so deleting it is always safe regardless of which scope triggered the prune.
    private const int PruneBatchSize = 500;
    private static readonly TimeSpan PruneCutoff = TimeSpan.FromHours(2);

    public async Task<bool> TryAcquireAsync(
        IReadOnlyList<AuthIssuanceThrottleRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            throw new ArgumentException("At least one throttle request is required.", nameof(requests));

        var nowUtc = clock.UtcNow;

        // Hash first, then lock-order by (scope, keyHash) — concurrent multi-key acquisitions
        // (e.g. two 094-2 invite calls racing on the same recipient+account pair, or on two
        // pairs that happen to share one partition) always take row locks in the same global
        // order, so this can never deadlock against itself.
        var ordered = requests
            .Select(r => (
                r.Scope,
                KeyHash: Hash(r.Key),
                r.PermitLimit,
                WindowStart: FloorToWindow(nowUtc, r.Window)))
            .OrderBy(r => r.Scope, StringComparer.Ordinal)
            .ThenBy(r => r.KeyHash, StringComparer.Ordinal)
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var r in ordered)
        {
            var acquired = await db.Database.SqlQuery<int>($"""
                INSERT INTO auth_issuance_throttles (scope, key_hash, window_start_utc, count)
                VALUES ({r.Scope}, {r.KeyHash}, {r.WindowStart}, 1)
                ON CONFLICT (scope, key_hash) DO UPDATE SET
                    window_start_utc = CASE
                        WHEN auth_issuance_throttles.window_start_utc < {r.WindowStart} THEN {r.WindowStart}
                        ELSE auth_issuance_throttles.window_start_utc
                    END,
                    count = CASE
                        WHEN auth_issuance_throttles.window_start_utc < {r.WindowStart} THEN 1
                        ELSE auth_issuance_throttles.count + 1
                    END
                WHERE auth_issuance_throttles.window_start_utc < {r.WindowStart}
                   OR auth_issuance_throttles.count < {r.PermitLimit}
                RETURNING count AS "Value"
                """).ToListAsync(cancellationToken);

            // An empty result means the ON CONFLICT DO UPDATE's WHERE clause evaluated false —
            // this partition is already at its permit limit, so Postgres left the row untouched
            // and RETURNING produced nothing for it. Roll back so no earlier partition in this
            // same acquisition keeps its provisional increment (BL154's all-or-nothing contract).
            if (acquired.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Best-effort only — the acquire above already committed, so a prune failure must not
        // turn a successful (and already-counted) acquisition into a request failure.
        try
        {
            await PruneExpiredAsync(nowUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "auth_issuance_throttles prune failed; will retry on a later acquisition.");
        }

        return true;
    }

    private async Task PruneExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - PruneCutoff;

        // ctid (Postgres physical row identifier) lets the DELETE target a bounded batch without
        // a surrogate id column — the table's primary key is the (scope, key_hash) pair itself.
        // FOR UPDATE SKIP LOCKED mirrors EfFeedbackPersistence's retention sweep: concurrent API
        // instances cooperate on the same expired rows rather than blocking each other.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM auth_issuance_throttles
            WHERE ctid IN (
                SELECT ctid FROM auth_issuance_throttles
                WHERE window_start_utc < {cutoff}
                ORDER BY ctid
                LIMIT {PruneBatchSize}
                FOR UPDATE SKIP LOCKED
            )
            """, cancellationToken);
    }

    private static DateTime FloorToWindow(DateTime nowUtc, TimeSpan window)
    {
        var ticks = nowUtc.Ticks - (nowUtc.Ticks % window.Ticks);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static string Hash(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
