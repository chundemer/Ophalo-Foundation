using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Readiness dependency check: can the API reach the database. The public response reports only
/// healthy/unhealthy — no connection string, host, or exception detail — but an outage is still
/// logged internally (redacted) so it is diagnosable.
/// </summary>
public sealed class DatabaseHealthCheck(OpHaloDbContext dbContext, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await dbContext.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Healthy();

            logger.LogError("Database readiness check failed: connection could not be established.");
            return HealthCheckResult.Unhealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Exception type only — never the message, which can include the connection string.
            logger.LogError("Database readiness check failed with {ExceptionType}.", ex.GetType().Name);
            return HealthCheckResult.Unhealthy();
        }
    }
}
