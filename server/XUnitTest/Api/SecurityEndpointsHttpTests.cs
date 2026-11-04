using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Controllers;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.ApiTests;

/// <summary>
/// The security endpoints over HTTP: routing, model binding, [ApiController] validation and
/// authorization, which the controller unit tests skip. A Construct sign-out on stg sent
/// <c>{"reason": ...}</c> without a userId and was refused by that validation with 400
/// "The UserId field is required." before the action ran.
/// Who may act on whom: a project's own token (Construct) on its own user only, the OS console on
/// other users under the access grant, a client credential on no one.
/// </summary>
public sealed class SecurityEndpointsHttpTests : IAsyncDisposable
{
    private const string Caller = "user-1";
    private const string OtherUser = "user-2";

    private readonly Mock<ISecurityQueryService> _query = new();
    private readonly Mock<ISessionRevocationService> _revocation = new();
    private readonly Mock<IActivityQueryService> _activity = new();
    private readonly Mock<IAccessGrantService> _accessGrant = new();
    private WebApplication? _app;

    public SecurityEndpointsHttpTests()
    {
        _query.Setup(q => q.ResolveCurrentSessionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("current-session");
        _query.Setup(q => q.GetUserSessionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserSessionDto>());
        _revocation.Setup(r => r.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sessionId, string _, string _, string? _, CancellationToken _) =>
                new RevokeSessionResponse { SessionId = sessionId });
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }
    }

    // ---------- a project's own token (Construct) ----------

    [Fact]
    public async Task ConstructSignOut_WithoutUserId_RevokesTheCallersOwnSession()
    {
        var client = await StartAsync(ProjectUser());

        // The stg request: a session id that ends in an encoded "=", a reason and no userId.
        var response = await client.PostAsync(
            "/security/sessions/ZuBg4VKh7auNY_Cw6a9C2VXZQt5SgfiGToVmY-P52a8%3D/revoke",
            Json("""{"reason":"Signed out from Construct OS profile"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _revocation.Verify(r => r.RevokeSessionAsync(
            "ZuBg4VKh7auNY_Cw6a9C2VXZQt5SgfiGToVmY-P52a8=", Caller, "current-session",
            "Signed out from Construct OS profile", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConstructSignOut_WithItsOwnUserId_RevokesTheCallersOwnSession()
    {
        var client = await StartAsync(ProjectUser());

        var response = await client.PostAsync("/security/sessions/sess-1/revoke", Json($$"""{"userId":"{{Caller}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _revocation.Verify(r => r.RevokeSessionAsync("sess-1", Caller, "current-session", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignOut_WithAnEmptyBody_RevokesTheCallersOwnSession()
    {
        var client = await StartAsync(ProjectUser());

        var response = await client.PostAsync("/security/sessions/sess-1/revoke", Json(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _revocation.Verify(r => r.RevokeSessionAsync("sess-1", Caller, "current-session", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConstructSignOut_OfAnotherUser_IsForbidden_EvenWhenTheAccessGrantWouldAllow()
    {
        _accessGrant.Setup(a => a.AuthorizeManageUserAsync(It.IsAny<string>())).ReturnsAsync(true);
        var client = await StartAsync(ProjectUser());

        var response = await client.PostAsync("/security/sessions/their-session/revoke", Json($$"""{"userId":"{{OtherUser}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Should().Be(AccessGrantErrors.GrantNotAllowed);
        _revocation.Verify(r => r.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConstructRevokingAnotherUsersRefreshToken_IsForbidden()
    {
        var client = await StartAsync(ProjectUser());

        var response = await client.PostAsync("/security/refresh-tokens/rt-9/revoke", Json($$"""{"userId":"{{OtherUser}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _revocation.Verify(r => r.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Construct_ReadsItsOwnSessions_ButNotAnotherUsers()
    {
        var client = await StartAsync(ProjectUser());

        var own = await client.GetAsync("/security/sessions");
        var theirs = await client.GetAsync($"/security/sessions?uid={OtherUser}");

        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        theirs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _query.Verify(q => q.GetUserSessionsAsync(Caller, It.IsAny<CancellationToken>()), Times.Once);
        _query.Verify(q => q.GetUserSessionsAsync(OtherUser, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- a client credential (no user) ----------

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"userId":"user-2"}""")]
    public async Task ClientCredentialSignOut_IsForbidden(string body)
    {
        _accessGrant.Setup(a => a.IsConsoleCaller()).Returns(true);
        _accessGrant.Setup(a => a.AuthorizeManageUserAsync(It.IsAny<string>())).ReturnsAsync(true);
        var client = await StartAsync(ClientCredential());

        var response = await client.PostAsync("/security/sessions/sess-1/revoke", Json(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _revocation.Verify(r => r.RevokeSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- the OS console ----------

    [Fact]
    public async Task ConsoleSignOut_OfAnotherUser_ProceedsUnderTheAccessGrant()
    {
        _accessGrant.Setup(a => a.IsConsoleCaller()).Returns(true);
        _accessGrant.Setup(a => a.AuthorizeManageUserAsync(OtherUser)).ReturnsAsync(true);
        var client = await StartAsync(ConsoleUser());

        var response = await client.PostAsync(
            "/security/sessions/their-session/revoke", Json($$"""{"userId":"{{OtherUser}}","reason":"admin"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _revocation.Verify(r => r.RevokeSessionAsync("their-session", OtherUser, "current-session", "admin", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- a revoke that matches nothing ----------

    [Fact]
    public async Task SignOut_OfASessionTheUserDoesNotHave_IsBadRequestSessionNotFound()
    {
        _revocation.Setup(r => r.RevokeSessionAsync("unknown", Caller, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevokeSessionResponse { SessionId = "unknown", Warnings = ["session_not_found"] });
        var client = await StartAsync(ProjectUser());

        var response = await client.PostAsync("/security/sessions/unknown/revoke", Json("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Should().Be("session_not_found");
    }

    // ---------- host ----------

    private async Task<HttpClient> StartAsync(BlocksContext caller)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(SecurityController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlySecurityController()));
        builder.Services.AddSingleton(_query.Object);
        builder.Services.AddSingleton(_revocation.Object);
        builder.Services.AddSingleton(_activity.Object);
        builder.Services.AddSingleton(_accessGrant.Object);
        builder.Services.AddAuthentication(SignedIn.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.Scheme, _ => { });
        // Genesis' ProtectedEndPoint is [Authorize(Policy = "Protected")]; its permission lookup is not under test.
        builder.Services.AddAuthorization(o => o.AddPolicy("Protected", p => p.RequireAuthenticatedUser()));

        _app = builder.Build();
        // What Genesis does for a real token: the caller's BlocksContext for the rest of the request.
        _app.Use(async (_, next) =>
        {
            BlocksContext.SetContext(caller);
            await next();
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    private static BlocksContext ProjectUser() => Context(Caller, impersonated: false);

    private static BlocksContext ConsoleUser() => Context("os-admin", impersonated: true);

    private static BlocksContext ClientCredential() => Context(userId: null, impersonated: false, clientId: "client-1");

    private static BlocksContext Context(string? userId, bool impersonated, string? clientId = null) =>
        BlocksContext.Create(
            tenantId: "tenant-1", roles: null, userId: userId, isAuthenticated: true,
            requestUri: "https://test/security", organizationId: "org-1", expireOn: DateTime.UtcNow.AddHours(1),
            email: null, permissions: null, userName: null, phoneNumber: null, displayName: null,
            oauthToken: null, originalTenantId: "tenant-1", applicationDomain: "test",
            impersonated: impersonated, clientId: clientId);

    private sealed class OnlySecurityController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(SecurityController).GetTypeInfo());
        }
    }

    private sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "SignedIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(Scheme)), Scheme)));
    }
}
