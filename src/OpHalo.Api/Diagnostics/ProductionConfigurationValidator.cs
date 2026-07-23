using Microsoft.Extensions.Configuration;

namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Fail-fast check for required production configuration (GAP-039a). Deliberately does not make
/// a live Resend API call — it only checks that values are present and superficially well-formed.
/// Extracted from Program.cs so the missing-key logic is unit-testable without booting the host.
/// </summary>
public static class ProductionConfigurationValidator
{
    /// <summary>
    /// Returns the list of missing/invalid required keys, empty if configuration is valid.
    /// Railway must supply the database connection as <c>ConnectionStrings__DefaultConnection</c>
    /// (the double-underscore ASP.NET Core config delimiter) — setting only Railway's own
    /// <c>DATABASE_URL</c> variable does not populate this key, and the API will fail to start.
    /// </summary>
    public static IReadOnlyList<string> GetMissingKeys(IConfiguration configuration)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            missing.Add("ConnectionStrings__DefaultConnection (Railway's DATABASE_URL is not read directly)");

        if (string.IsNullOrWhiteSpace(configuration["App:PublicBaseUrl"]))
            missing.Add("App:PublicBaseUrl");

        if (string.IsNullOrWhiteSpace(configuration["Resend:ApiKey"]))
            missing.Add("Resend:ApiKey");

        var fromAddress = configuration["Resend:FromAddress"];
        if (string.IsNullOrWhiteSpace(fromAddress))
            missing.Add("Resend:FromAddress");
        else if (!fromAddress.Contains('@'))
            missing.Add("Resend:FromAddress (not a valid address)");

        return missing;
    }

    public static void ValidateOrThrow(IConfiguration configuration)
    {
        var missing = GetMissingKeys(configuration);
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Required production configuration is missing or invalid: {string.Join(", ", missing)}.");
    }
}
