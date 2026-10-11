using System.Net;
using Kuechenfuchs.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class ReverseProxySettingsTests
{
    private const string TrustedProxy = "172.31.240.10";

    [Fact]
    public async Task AcceptsForwardedHeadersFromTheConfiguredProxy()
    {
        var context = await SendAsync(TrustedProxy, new() { TrustForwardedHeaders = true, KnownProxies = TrustedProxy });

        Assert.Equal("https", context.Request.Scheme);
        Assert.True(context.Request.IsHttps);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("172.31.240.11")] // other container in the same Compose network
    [InlineData("172.17.0.1")]    // Docker bridge gateway
    [InlineData("10.0.0.5")]      // other private network
    [InlineData("192.168.1.20")]  // LAN device
    [InlineData("203.0.113.50")]  // public address
    public async Task IgnoresSpoofedForwardedHeadersFromUntrustedSenders(string sender)
    {
        var context = await SendAsync(sender, new() { TrustForwardedHeaders = true, KnownProxies = TrustedProxy });

        Assert.Equal("http", context.Request.Scheme);
        Assert.False(context.Request.IsHttps);
        Assert.Equal(IPAddress.Parse(sender), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task EvaluatesOnlyTheDirectlyConnectedProxyHop()
    {
        // A trusted proxy forwarding a client-supplied chain must not make earlier, spoofed entries trusted.
        var context = await SendAsync(
            TrustedProxy,
            new() { TrustForwardedHeaders = true, KnownProxies = TrustedProxy },
            forwardedFor: "198.51.100.1, 172.31.240.10");

        Assert.Equal(IPAddress.Parse("172.31.240.10"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task AcceptsNarrowConfiguredNetwork()
    {
        var context = await SendAsync("172.31.240.42", new() { TrustForwardedHeaders = true, KnownNetworks = "172.31.240.0/24" });

        Assert.Equal("https", context.Request.Scheme);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("not-an-ip", "")]
    [InlineData("0.0.0.0", "")]
    [InlineData("", "10.0.0.0/8")]
    [InlineData("", "172.16.0.0/12")]
    [InlineData("", "192.168.0.0/16")]
    [InlineData("", "0.0.0.0/0")]
    [InlineData("", "fd00::/8")]
    public void RejectsMissingInvalidOrBroadTrust(string proxies, string networks)
    {
        var settings = new ReverseProxySettings { TrustForwardedHeaders = true, KnownProxies = proxies, KnownNetworks = networks };

        Assert.Throws<InvalidOperationException>(() => settings.Apply(new ForwardedHeadersOptions()));
    }

    private static async Task<HttpContext> SendAsync(
        string sender,
        ReverseProxySettings settings,
        string forwardedFor = "203.0.113.7")
    {
        var options = new ForwardedHeadersOptions();
        settings.Apply(options);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Connection.RemoteIpAddress = IPAddress.Parse(sender);
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Host"] = "evil.example";
        context.Request.Host = new HostString("kuechenfuchs.zaddach.org");
        await middleware.Invoke(context);
        Assert.Equal("kuechenfuchs.zaddach.org", context.Request.Host.Value);
        return context;
    }
}
