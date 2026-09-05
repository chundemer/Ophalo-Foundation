using Microsoft.Extensions.Configuration;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Keep.Infrastructure.PriceBook;

/// <summary>
/// Configuration-backed <see cref="IReleaseGatePolicy"/> (BL142 Session 1). A single global switch,
/// not a per-account row: Proposed Work/Quote release is a one-time, all-accounts product launch
/// event, not an individual commercial exception (unlike package entitlement, which is legitimately
/// per-account). Deliberately owned by deploy-time configuration rather than a runtime admin
/// action — release is an engineering-owned event. No checked-in configuration sets this true; the
/// key is absent from every committed appsettings file, so every environment fails closed until an
/// explicit environment-level override is made.
/// </summary>
public sealed class ConfigurationReleaseGatePolicy(IConfiguration configuration) : IReleaseGatePolicy
{
    private const string ProposedWorkReleasedKey = "Keep:ReleaseGates:ProposedWorkQuotes";

    /// <summary>
    /// Reads the raw string and parses it explicitly rather than <c>IConfiguration.GetValue&lt;bool?&gt;</c>,
    /// which throws on a malformed value instead of failing closed. Missing key, malformed value,
    /// or any value other than a valid "true" all return false.
    /// </summary>
    public bool IsProposedWorkReleased()
    {
        var raw = configuration[ProposedWorkReleasedKey];
        return bool.TryParse(raw, out var value) && value;
    }
}
