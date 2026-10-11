using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;

namespace Kuechenfuchs.Api.Hosting;

/// <summary>
/// Trust for X-Forwarded-Proto/-For behind a TLS-terminating proxy. Only explicitly configured, actually
/// controlled proxy addresses (or narrow networks) are trusted; X-Forwarded-Host is never evaluated.
/// </summary>
public sealed class ReverseProxySettings
{
    public const string SectionName = "ReverseProxy";
    private const int MinimumIPv4Prefix = 24;
    private const int MinimumIPv6Prefix = 64;

    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Comma- or space-separated IP addresses of the proxy that talks to the API directly.</summary>
    public string KnownProxies { get; set; } = string.Empty;

    /// <summary>Optional comma- or space-separated CIDR networks (IPv4 at least /24, IPv6 at least /64).</summary>
    public string KnownNetworks { get; set; } = string.Empty;

    /// <summary>Applies the settings; throws on missing, invalid or too broad trust configuration.</summary>
    public void Apply(ForwardedHeadersOptions options)
    {
        var proxies = Split(KnownProxies).Select(ParseAddress).ToList();
        var networks = Split(KnownNetworks).Select(ParseNetwork).ToList();
        if (proxies.Count == 0 && networks.Count == 0)
        {
            throw new InvalidOperationException(
                "ReverseProxy:TrustForwardedHeaders requires ReverseProxy:KnownProxies or ReverseProxy:KnownNetworks.");
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Only the directly connected, trusted proxy is evaluated; it is responsible for its own upstream trust.
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IPAddress ParseAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || value.Contains('/'))
        {
            throw new InvalidOperationException($"ReverseProxy:KnownProxies contains an invalid IP address: '{value}'.");
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            throw new InvalidOperationException("ReverseProxy:KnownProxies must not contain an unspecified address.");
        }

        return address;
    }

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network))
        {
            throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains an invalid CIDR network: '{value}'.");
        }

        var minimumPrefix = network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? MinimumIPv4Prefix : MinimumIPv6Prefix;
        if (network.PrefixLength < minimumPrefix)
        {
            throw new InvalidOperationException(
                $"ReverseProxy:KnownNetworks entry '{value}' is too broad; configure the concrete proxy address or a network of at least /{minimumPrefix}.");
        }

        return network;
    }
}
