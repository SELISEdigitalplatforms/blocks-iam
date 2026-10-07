using Api.Middleware;
using Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace XUnitTest.ApiTests;

/// <summary>
/// The per-response style nonce: the middleware puts the same value in the CSP header and in
/// <see cref="HttpContext.Items"/>, where the SPA shell renderer reads it.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    private const string Policy = "default-src 'self'; style-src 'self' 'nonce-__CSP_STYLE_NONCE__';";

    private static readonly StyleNonce Nonces = new(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public async Task InvokeAsync_PutsTheItemNonceInTheHeader()
    {
        var (context, start) = NewContext("/login");
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        var nonce = context.Items[SecurityHeadersMiddleware.StyleNonceItemKey].Should().BeOfType<string>().Subject;
        nonce.Should().NotBeNullOrEmpty();
        context.Response.Headers.ContentSecurityPolicy.ToString()
            .Should().Contain($"style-src 'self' 'nonce-{nonce}'")
            .And.NotContain(ContentSecurityPolicy.StyleNoncePlaceholder);
    }

    [Fact]
    public async Task InvokeAsync_KeepsThePolicyAnEndpointAlreadySet()
    {
        var (context, start) = NewContext("/api/iam/something");
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            return Task.CompletedTask;
        }, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Be("default-src 'none'");
    }

    [Fact]
    public async Task InvokeAsync_GivesTheSameBrowserTheSameNonce()
    {
        var seed = StyleNonce.NewSeed();
        var nonces = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var (context, start) = NewContext("/login");
            context.Request.Headers.Cookie = $"{StyleNonce.SeedCookieName}={seed}";
            await new SecurityHeadersMiddleware(_ => Task.CompletedTask, Policy, Nonces).InvokeAsync(context);
            await start();
            nonces.Add((string)context.Items[SecurityHeadersMiddleware.StyleNonceItemKey]!);
        }

        nonces[0].Should().Be(nonces[1]).And.Be(Nonces.For(seed));
    }

    [Theory]
    [InlineData("/oidc/login")]
    [InlineData("/oidc/error")]
    [InlineData("/oidc/invitation")]
    public async Task InvokeAsync_DoesNotLetHostedOidcPagesBeCached(string path)
    {
        var (context, start) = NewContext(path);
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return Task.CompletedTask;
        }, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        context.Response.Headers.CacheControl.ToString().Should().Be("no-store, no-cache, must-revalidate, max-age=0");
        context.Response.Headers.Pragma.ToString().Should().Be("no-cache");
    }

    [Theory]
    [InlineData("/oidc/authorize", "GET", null)]
    [InlineData("/oidc/login", "POST", "text/html; charset=utf-8")]
    [InlineData("/api/oidc/token", "GET", "application/json")]
    [InlineData("/api/oidc/login", "GET", "text/html; charset=utf-8")]
    public async Task InvokeAsync_LeavesCookieIssuingOidcResponsesAlone(string path, string method, string? contentType)
    {
        var (context, start) = NewContext(path);
        context.Request.Method = method;
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.ContentType = contentType;
            return Task.CompletedTask;
        }, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        context.Response.Headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_KeepsCacheControlAnOidcPageAlreadySet()
    {
        var (context, start) = NewContext("/oidc/login");
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers.CacheControl = "private";
            return Task.CompletedTask;
        }, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        context.Response.Headers.CacheControl.ToString().Should().Be("private");
    }

    [Theory]
    [InlineData("/api/oidc")]
    [InlineData("/api/idp")]
    [InlineData("/login")]
    [InlineData("/api/auth/token")]
    public async Task InvokeAsync_KeepsTheSpaShellUncachedOnAuthPaths(string path)
    {
        var (context, start) = NewContext(path);
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            // What the SPA fallback does when it answers an auth-prefixed path.
            ctx.Response.ContentType = "text/html; charset=utf-8";
            SecurityHeadersMiddleware.DisableCaching(ctx.Response.Headers);
            return Task.CompletedTask;
        }, Policy, Nonces);

        await middleware.InvokeAsync(context);
        await start();

        context.Response.Headers.CacheControl.ToString().Should().Be("no-store, no-cache, must-revalidate, max-age=0");
        context.Response.Headers.Pragma.ToString().Should().Be("no-cache");
    }

    [Fact]
    public void DisableCaching_RejectsNullHeaders()
    {
        var act = () => SecurityHeadersMiddleware.DisableCaching(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static (HttpContext Context, Func<Task> Start) NewContext(string path)
    {
        var response = new StartableResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        return (context, response.StartAsync);
    }

    /// <summary>Runs the OnStarting callbacks on demand, the way the server does before the first write.</summary>
    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in Enumerable.Reverse(_onStarting))
            {
                await callback(state);
            }
        }
    }
}
