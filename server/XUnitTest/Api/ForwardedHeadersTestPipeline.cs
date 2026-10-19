using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.ApiTests;

/// <summary>
/// Builds the request pipeline the way Program.cs does: <see cref="ForwardedHeadersSetup.Apply"/>
/// first, then a probe standing in for every later middleware/endpoint, which records the
/// <c>Connection.RemoteIpAddress</c> it observes.
/// </summary>
internal static class ForwardedHeadersTestPipeline
{
    /// <summary>The dev gateway instances (10.10.73.4-.6) sit in this network in the tests.</summary>
    public const string GatewayNetwork = "10.10.73.0/24";
    public const string GatewayAddress = "10.10.73.4";

    public static IConfiguration Config(params string[] knownNetworks)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < knownNetworks.Length; i++)
        {
            values[$"ForwardedHeaders:KnownNetworks:{i}"] = knownNetworks[i];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static RequestDelegate Build(ForwardedHeadersOptions? options, Action<HttpContext>? probe = null)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        ForwardedHeadersSetup.Apply(app, options);
        app.Run(ctx =>
        {
            probe?.Invoke(ctx);
            return Task.CompletedTask;
        });
        return app.Build();
    }

    public static DefaultHttpContext NewContext(string? remoteIp, string? forwardedFor = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("iam.internal");
        if (forwardedFor is not null)
        {
            ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        return ctx;
    }

    /// <summary>
    /// A request that came through the trusted gateway carrying <paramref name="forwardedFor"/>,
    /// after the configured middleware has run (what every IP writer sees in production).
    /// </summary>
    public static async Task<DefaultHttpContext> ThroughTrustedGatewayAsync(string forwardedFor)
    {
        var ctx = NewContext(GatewayAddress, forwardedFor);
        await Build(ForwardedHeadersSetup.BuildOptions(Config(GatewayNetwork)))(ctx);
        return ctx;
    }
}
