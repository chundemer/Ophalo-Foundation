using System.Net;

namespace OpHalo.Api.Helpers;

public static class ClientIpResolver
{
    public static string Resolve(HttpContext context, IReadOnlyList<IPNetwork> trustedProxies) =>
        Resolve(
            context.Connection.RemoteIpAddress,
            context.Request.Headers["CF-Connecting-IP"].FirstOrDefault(),
            context.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
            trustedProxies);

    public static string Resolve(
        IPAddress? remoteAddress,
        string? cfConnectingIp,
        string? xForwardedFor,
        IReadOnlyList<IPNetwork> trustedProxies)
    {
        if (remoteAddress is null)
            return "unknown";

        // Normalize IPv4-mapped IPv6 (::ffff:a.b.c.d) so CIDR trust checks against
        // IPv4 networks (e.g. 127.0.0.1/32) work regardless of how the OS presents the address.
        var normalized = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;

        if (!IsTrusted(normalized, trustedProxies))
            return normalized.ToString();

        if (!string.IsNullOrWhiteSpace(cfConnectingIp) &&
            IPAddress.TryParse(cfConnectingIp.Trim(), out var cfAddr))
            return cfAddr.ToString();

        if (!string.IsNullOrWhiteSpace(xForwardedFor))
        {
            var first = xForwardedFor.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var xffAddr))
                return xffAddr.ToString();
        }

        return normalized.ToString();
    }

    private static bool IsTrusted(IPAddress address, IReadOnlyList<IPNetwork> trustedProxies)
    {
        foreach (var network in trustedProxies)
            if (network.Contains(address))
                return true;
        return false;
    }
}
