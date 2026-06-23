namespace OpHalo.Api.Helpers;

/// <summary>
/// Redacts public bearer-token segments from request paths before any application logging.
/// Token-bearing paths must never appear in raw form in application logs/traces (GAP-013).
/// </summary>
public static class PublicTokenPathRedactor
{
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path ?? string.Empty;

        if (path.StartsWith("/keep/public-intake/token/", StringComparison.OrdinalIgnoreCase))
            return "/keep/public-intake/token/[redacted]";

        if (path.StartsWith("/continuity/public-intake/token/", StringComparison.OrdinalIgnoreCase))
            return "/continuity/public-intake/token/[redacted]";

        if (path.StartsWith("/keep/r/", StringComparison.OrdinalIgnoreCase))
        {
            var after = path.AsSpan("/keep/r/".Length);
            var slash = after.IndexOf('/');
            return slash < 0
                ? "/keep/r/[redacted]"
                : $"/keep/r/[redacted]{after[slash..]}";
        }

        return path;
    }
}
