using System.Net;
using OpHalo.Api.Helpers;

namespace OpHalo.UnitTests;

public sealed class ClientIpResolverTests
{
    private static readonly IReadOnlyList<IPNetwork> NoTrust = [];
    private static readonly IReadOnlyList<IPNetwork> TrustLoopback =
        [IPNetwork.Parse("127.0.0.1/32"), IPNetwork.Parse("::1/128")];
    private static readonly IReadOnlyList<IPNetwork> TrustPrivate10 =
        [IPNetwork.Parse("10.0.0.0/8")];

    [Fact]
    public void NullRemote_ReturnsUnknown()
    {
        var result = ClientIpResolver.Resolve(null, "1.2.3.4", null, TrustLoopback);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void UntrustedRemote_ReturnsSelf_IgnoresCfHeader()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Parse("203.0.113.5"), "1.2.3.4", null, NoTrust);
        Assert.Equal("203.0.113.5", result);
    }

    [Fact]
    public void UntrustedRemote_ReturnsSelf_IgnoresXff()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Parse("203.0.113.5"), null, "1.2.3.4", NoTrust);
        Assert.Equal("203.0.113.5", result);
    }

    [Fact]
    public void TrustedRemote_ValidCf_ReturnsCfIp()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "203.0.113.1", null, TrustLoopback);
        Assert.Equal("203.0.113.1", result);
    }

    [Fact]
    public void TrustedRemote_CfTakesPrecedenceOverXff()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "203.0.113.1", "9.9.9.9", TrustLoopback);
        Assert.Equal("203.0.113.1", result);
    }

    [Fact]
    public void TrustedRemote_NoCf_ValidXff_ReturnsFirstXffEntry()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, null, "203.0.113.2, 10.0.0.1", TrustLoopback);
        Assert.Equal("203.0.113.2", result);
    }

    [Fact]
    public void TrustedRemote_NoCf_NoXff_FallsBackToRemote()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, null, null, TrustLoopback);
        Assert.Equal(IPAddress.Loopback.ToString(), result);
    }

    [Fact]
    public void TrustedRemote_BlankCf_ValidXff_ReturnsXff()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "  ", "203.0.113.3", TrustLoopback);
        Assert.Equal("203.0.113.3", result);
    }

    [Fact]
    public void TrustedRemote_MalformedCf_ValidXff_ReturnsXff()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "not-an-ip", "203.0.113.4, 10.0.0.2", TrustLoopback);
        Assert.Equal("203.0.113.4", result);
    }

    [Fact]
    public void TrustedRemote_MalformedCf_MalformedXff_FallsBackToRemote()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "bad", "also-bad", TrustLoopback);
        Assert.Equal(IPAddress.Loopback.ToString(), result);
    }

    [Fact]
    public void TrustedRemote_CfWithSurroundingWhitespace_TrimsAndParses()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "  203.0.113.5  ", null, TrustLoopback);
        Assert.Equal("203.0.113.5", result);
    }

    [Fact]
    public void Ipv6Loopback_TrustedByIpv6Network_UsesCfHeader()
    {
        var result = ClientIpResolver.Resolve(IPAddress.IPv6Loopback, "2001:db8::1", null, TrustLoopback);
        Assert.Equal("2001:db8::1", result);
    }

    [Fact]
    public void PrivateAddress_TrustedByCidr_UsesCfHeader()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Parse("10.42.1.7"), "203.0.113.6", null, TrustPrivate10);
        Assert.Equal("203.0.113.6", result);
    }

    [Fact]
    public void EmptyTrustedList_AlwaysReturnsRemote()
    {
        var result = ClientIpResolver.Resolve(IPAddress.Loopback, "1.2.3.4", "5.6.7.8", NoTrust);
        Assert.Equal(IPAddress.Loopback.ToString(), result);
    }

    [Fact]
    public void Ipv4MappedLoopback_TrustedByIpv4Network_UsesCfHeader()
    {
        // ::ffff:127.0.0.1 is normalized to 127.0.0.1, which is in 127.0.0.1/32.
        var mapped = IPAddress.Loopback.MapToIPv6();
        var result = ClientIpResolver.Resolve(mapped, "203.0.113.7", null, TrustLoopback);
        Assert.Equal("203.0.113.7", result);
    }

    [Fact]
    public void Ipv4MappedUntrustedRemote_ReturnsNormalizedIpv4()
    {
        // ::ffff:203.0.113.8 normalizes to 203.0.113.8, which is not trusted.
        var mapped = IPAddress.Parse("203.0.113.8").MapToIPv6();
        var result = ClientIpResolver.Resolve(mapped, "1.2.3.4", null, TrustLoopback);
        Assert.Equal("203.0.113.8", result);
    }
}
