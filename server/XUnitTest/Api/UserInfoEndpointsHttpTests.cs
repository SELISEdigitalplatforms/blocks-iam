using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Controllers;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Authentication.DomainService.SignupLinks;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using IAuthenticationService = Authentication.DomainService.Authentication.IAuthenticationService;

namespace XUnitTest.ApiTests;

/// <summary>
/// auth/userinfo and auth/me over HTTP, with the SPA fallback mapped as Program.cs maps it.
/// Prod 2026-11-09: discovery advertised auth/userinfo while only auth/me existed, so SonarQube's
/// UserInfo call fell through to the fallback and got 200 text/html. Here both routes must reach
/// their own action, userinfo on GET and POST (OpenID Connect Core 1.0 §5.3), and never the SPA.
/// </summary>
public sealed class UserInfoEndpointsHttpTests : IAsyncDisposable
{
    private const string GoodToken = "good";

    private readonly Mock<IAuthenticationService> _authService = new();
    private WebApplication? _app;

    public UserInfoEndpointsHttpTests()
    {
        _authService.Setup(s => s.BuildStandardOidcUserInfoAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new Dictionary<string, object> { ["sub"] = "user-1", ["preferred_username"] = "jane@x.com" });
        _authService.Setup(s => s.BuildOidcUserInfoAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((true, new Dictionary<string, object> { ["sub"] = "user-1", ["tenant_id"] = "tenant-1" }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetUserInfo_WithToken_ReturnsTheStandardClaimsAsJson()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/auth/userinfo", GoodToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await Body(response);
        body.GetProperty("sub").GetString().Should().Be("user-1");
        body.GetProperty("preferred_username").GetString().Should().Be("jane@x.com");
        body.TryGetProperty("tenant_id", out _).Should().BeFalse();
        _authService.Verify(s => s.BuildOidcUserInfoAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task PostUserInfo_WithToken_ReturnsTheSameClaims()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Request(HttpMethod.Post, "/auth/userinfo", GoodToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await Body(response)).GetProperty("sub").GetString().Should().Be("user-1");
    }

    [Fact]
    public async Task GetMe_WithToken_StillReturnsTheBlocksShape()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/auth/me", GoodToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("tenant_id").GetString().Should().Be("tenant-1");
        _authService.Verify(s => s.BuildStandardOidcUserInfoAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task UserInfo_WithoutToken_Returns401_NotTheSpa(string method)
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Request(new HttpMethod(method), "/auth/userinfo", token: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<html");
    }

    [Fact]
    public async Task UnknownAuthPath_FallsThroughToTheSpa_AsBeforeTheFix()
    {
        // Guards the harness itself: without a real userinfo route this is what SonarQube got.
        var client = await StartAsync();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/auth/not-a-route", GoodToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    // ---------- host ----------

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(AuthenticationController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyAuthenticationController()));
        builder.Services.AddSingleton(_authService.Object);
        builder.Services.AddSingleton(Mock.Of<IAccountService>());
        builder.Services.AddSingleton(Mock.Of<IAuthenticationFlowService>());
        builder.Services.AddSingleton(Mock.Of<IAuthenticationConfigurationService>());
        builder.Services.AddSingleton(Mock.Of<IAuthenticationDomainService>());
        builder.Services.AddSingleton(Mock.Of<ISignupLinkRedemptionOrchestrator>());
        builder.Services.AddSingleton(Mock.Of<IClientCredentialGrantGuard>());
        builder.Services.AddAuthentication(BearerGood.Scheme)
            .AddScheme<AuthenticationSchemeOptions, BearerGood>(BearerGood.Scheme, _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("Protected", p => p.RequireAuthenticatedUser()));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        // Program.cs: app.MapFallback(context => WriteSpaShell(context, indexHtmlTemplate));
        _app.MapFallback(context =>
        {
            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync("<!doctype html><html><title>Blocks IAM</title></html>");
        });
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private sealed class OnlyAuthenticationController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(AuthenticationController).GetTypeInfo());
        }
    }

    /// <summary>Stands in for Genesis' JWT validation: "Bearer good" is a valid token, anything else is none.</summary>
    private sealed class BearerGood(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "BearerGood";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization.ToString() != $"Bearer {GoodToken}")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", "user-1")], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
