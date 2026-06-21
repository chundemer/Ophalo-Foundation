using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

internal enum IntakeCommitOutcome
{
    Committed = 1,
    UniqueTokenCollision = 2,
    CustomerCanonicalPhoneCollision = 3
}

/// <summary>
/// Shared commit logic for both public and authenticated business intake.
/// Owns: new-customer detection, entity attachment, atomic save, constraint
/// classification, and tracker cleanup on collision.
/// Each persistence class maps the returned outcome to its own application enum.
/// </summary>
internal static class KeepIntakeCommitHelper
{
    internal static async Task<IntakeCommitOutcome> CommitAsync(
        OpHaloDbContext dbContext,
        KeepCustomer customer,
        KeepRequest request,
        KeepRequestEvent requestEvent,
        CancellationToken ct)
    {
        var customerIsNew = dbContext.Entry(customer).State == EntityState.Detached;

        if (customerIsNew)
            dbContext.Set<KeepCustomer>().Add(customer);

        dbContext.Set<KeepRequest>().Add(request);
        dbContext.Set<KeepRequestEvent>().Add(requestEvent);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return IntakeCommitOutcome.Committed;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && pg.SqlState == "23505"
            && pg.ConstraintName is
                "ix_keep_requests_page_token" or
                "ix_keep_requests_account_reference_code")
        {
            Detach(dbContext, customer, customerIsNew, request, requestEvent);
            return IntakeCommitOutcome.UniqueTokenCollision;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && pg.SqlState == "23505"
            && pg.ConstraintName == "ix_keep_customers_account_canonical_phone")
        {
            Detach(dbContext, customer, customerIsNew, request, requestEvent);
            return IntakeCommitOutcome.CustomerCanonicalPhoneCollision;
        }
    }

    private static void Detach(
        OpHaloDbContext dbContext,
        KeepCustomer customer, bool customerIsNew,
        KeepRequest request,
        KeepRequestEvent requestEvent)
    {
        dbContext.Entry(request).State      = EntityState.Detached;
        dbContext.Entry(requestEvent).State = EntityState.Detached;
        if (customerIsNew)
            dbContext.Entry(customer).State = EntityState.Detached;
    }
}
