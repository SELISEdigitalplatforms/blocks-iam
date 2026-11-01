using Blocks.Api;
using System.Diagnostics;
using System.Net;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.RequestModel;
using Microsoft.Extensions.Configuration;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;
using static XUnitTest.ApiTests.ForwardedHeadersTestPipeline;

namespace XUnitTest.ApiTests;

/// <summary>
/// SPEC "Persist real client IP on IdP session behind trusted proxies": the forwarded-header
/// configuration (A1), the middleware behaviour for spec Examples 1-6, and the real IP writers fed
/// by a request whose RemoteIpAddress the middleware resolved from a trusted X-Forwarded-For.
/// </summary>
public class ForwardedHeadersSetupTests : IDisposable
{
    private const string Client = "203.0.113.10";
    // The spoofable first entry: no writer may store it, so a call site reverting to the raw header fails.
    private const string Spoofed = "198.51.100.99";
    private const string MultiEntry = Spoofed + ", " + Client;
    private readonly ITestOutputHelper _output;

    public ForwardedHeadersSetupTests(ITestOutputHelper output)
    {
        _output = output;
        BlocksContext.IsTestMode = true;
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
            isAuthenticated: true, requestUri: "https://test", organizationId: "default",
            permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
            userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
            originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null);
        BlocksContext.IsTestMode = false;
    }

    // ---------- BuildOptions (A1, C2, C12) ----------

    [Fact]
    public void BuildOptions_C2_MissingSection_ReturnsNull_MiddlewareNotRegistered()
    {
        ForwardedHeadersSetup.BuildOptions(Config()).Should().BeNull();
    }

    [Fact]
    public void BuildOptions_C2_EmptyArrayInAppsettings_ReturnsNull()
    {
        // An empty JSON array (as in appsettings.json) binds to no children at all.
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("{\"ForwardedHeaders\":{\"KnownNetworks\":[]}}"u8.ToArray()))
            .Build();

        ForwardedHeadersSetup.BuildOptions(config).Should().BeNull();
    }

    [Fact]
    public void BuildOptions_A1_ValidList_XForwardedForOnly_ForwardLimit1_DefaultsCleared_NetworksExact()
    {
        var options = ForwardedHeadersSetup.BuildOptions(Config(GatewayNetwork, "fd00::/8"))!;

        options.Should().NotBeNull();
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor);
        options.ForwardLimit.Should().Be(1);
        options.KnownProxies.Should().BeEmpty("the loopback default proxy must not be trusted");
        options.KnownIPNetworks.Should().Equal(
            System.Net.IPNetwork.Parse(GatewayNetwork), System.Net.IPNetwork.Parse("fd00::/8"));
    }

    [Theory]
    [InlineData("10.10.73.0/24")]
    [InlineData("not-a-cidr")]
    public void BuildOptions_ScalarValueWithoutIndex_ThrowsNamingKeyAndIndexedForm(string scalar)
    {
        // ForwardedHeaders__KnownNetworks=<cidr> (no __0) binds to no array entries; it must not
        // silently leave the middleware unregistered.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks"] = scalar })
            .Build();

        var act = () => ForwardedHeadersSetup.BuildOptions(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ForwardedHeaders:KnownNetworks*")
            .WithMessage("*ForwardedHeaders__KnownNetworks__0=<cidr>*");
    }

    [Fact]
    public void BuildOptions_EmptyScalarValue_ReturnsNull()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks"] = "" })
            .Build();

        ForwardedHeadersSetup.BuildOptions(config).Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-cidr")]
    [InlineData("10.10.73.0/33")]
    [InlineData("10.10.73.4")]
    [InlineData(" ")]
    public void BuildOptions_C12_InvalidOrBlankEntry_ThrowsNamingTheEntry(string bad)
    {
        var act = () => ForwardedHeadersSetup.BuildOptions(Config(GatewayNetwork, bad));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"ForwardedHeaders:KnownNetworks entry '{bad}' is not a valid CIDR network.");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData(" 10 ", 10)]
    public void BuildOptions_ForwardLimit_ReadFromConfiguration(string configured, int expected)
    {
        var options = ForwardedHeadersSetup.BuildOptions(ConfigWithForwardLimit(configured, GatewayNetwork))!;

        options.ForwardLimit.Should().Be(expected);
    }

    [Fact]
    public void BuildOptions_ForwardLimit_UnsetOrBlank_DefaultsToOne()
    {
        ForwardedHeadersSetup.BuildOptions(ConfigWithForwardLimit("", GatewayNetwork))!.ForwardLimit.Should().Be(1);
        ForwardedHeadersSetup.BuildOptions(ConfigWithForwardLimit(null, GatewayNetwork))!.ForwardLimit.Should().Be(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("four")]
    public void BuildOptions_ForwardLimit_OutOfRangeOrNotANumber_ThrowsNamingTheKey(string bad)
    {
        var act = () => ForwardedHeadersSetup.BuildOptions(ConfigWithForwardLimit(bad, GatewayNetwork));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"ForwardedHeaders:ForwardLimit '{bad}' must be a whole number from 1 to 10.");
    }

    [Fact]
    public void Apply_NullOptions_RegistersNothing()
    {
        // With empty KnownProxies/KnownIPNetworks the middleware would trust every source, so a
        // null options object must leave the pipeline untouched.
        var observed = (IPAddress?)null;
        var pipeline = Build(null, ctx => observed = ctx.Connection.RemoteIpAddress);
        var ctx = NewContext("198.51.100.20", Client);

        pipeline(ctx);

        observed!.ToString().Should().Be("198.51.100.20");
        ctx.Request.Headers["X-Forwarded-For"].ToString().Should().Be(Client);
    }

    // Reads Program.cs source because the test project has no TestServer/WebApplicationFactory package;
    // guards that ForwardedHeadersSetup.Apply stays the first middleware after builder.Build().
    [Fact]
    public void Program_C10_ForwardedHeadersAreAppliedBeforeEveryOtherMiddleware()
    {
        var program = File.ReadAllText(FindProgramCs());
        var applyAt = program.IndexOf("ForwardedHeadersSetup.Apply(app,", StringComparison.Ordinal);
        var buildAt = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);

        applyAt.Should().BeGreaterThan(buildAt);
        var between = program[(buildAt + "var app = builder.Build();".Length)..applyAt];
        between.Should().NotContain("app.Use").And.NotContain("ConfigureMiddleware(");
        applyAt.Should().BeLessThan(program.IndexOf("app.UseMiddleware<SecurityHeadersMiddleware>", StringComparison.Ordinal));
        applyAt.Should().BeLessThan(program.IndexOf("ApplicationConfigurations.ConfigureMiddleware(", StringComparison.Ordinal));
    }

    private static string FindProgramCs()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("server/Api/Program.cs not found above the test output directory.");
    }

    // ---------- Middleware: spec Examples 1-6 ----------

    private static async Task<(DefaultHttpContext Ctx, string? Seen)> RunAsync(string[] networks, string? remoteIp, string? xff, Action<DefaultHttpContext>? arrange = null)
    {
        string? seen = "not-run";
        var pipeline = Build(ForwardedHeadersSetup.BuildOptions(Config(networks)), c => seen = c.Connection.RemoteIpAddress?.ToString());
        var ctx = NewContext(remoteIp, xff);
        arrange?.Invoke(ctx);
        await pipeline(ctx);
        return (ctx, seen);
    }

    [Fact]
    public async Task Example1_H1_H9_TrustedGateway_SingleEntry_LaterMiddlewareSeesClient()
    {
        var (_, seen) = await RunAsync(new[] { GatewayNetwork }, GatewayAddress, Client);
        seen.Should().Be(Client);
    }

    [Fact]
    public async Task Example2_C4_TrustedGateway_MultiEntry_LastOnly_OtherEntriesNotRecorded()
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, GatewayAddress, "198.51.100.99, " + Client);

        seen.Should().Be(Client);
        // ASP.NET semantics: the unconsumed (client-supplied) entry stays in X-Forwarded-For and the
        // gateway hop moves to X-Original-For. No IP writer reads either header any more (H12).
        ctx.Request.Headers["X-Forwarded-For"].ToString().Should().Be("198.51.100.99");
        ctx.Request.Headers["X-Original-For"].ToString().Should().StartWith(GatewayAddress);

        var ips = AuthDomain().GetVisitorsIpAddresses(ctx).ToList();
        ips.Should().Equal(Client);
        ips.Should().NotContain("198.51.100.99");
    }

    [Fact]
    public async Task Example3_H5_NoForwardedHeader_UsesConnectionAddress()
    {
        var (_, seen) = await RunAsync(new[] { GatewayNetwork }, "198.51.100.20", null);
        seen.Should().Be("198.51.100.20");
    }

    [Fact]
    public async Task Example4_C1_C9_UntrustedConnection_SpoofedHeaderIgnored()
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, "198.51.100.20", "203.0.113.99");

        seen.Should().Be("198.51.100.20");
        AuthDomain().GetVisitorsIpAddresses(ctx).Should().Equal("198.51.100.20");
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("203.0.113.10:abc")]
    [InlineData(",,,")]
    public async Task Example5_C3_MalformedHeader_FallsBackToConnection_NoThrow(string malformed)
    {
        var (_, seen) = await RunAsync(new[] { GatewayNetwork }, GatewayAddress, malformed);

        seen.Should().Be(GatewayAddress);
    }

    [Fact]
    public async Task Example6_C2_EmptyConfig_LoopbackNotTrusted()
    {
        var (_, seen) = await RunAsync(Array.Empty<string>(), "127.0.0.1", Client);
        seen.Should().Be("127.0.0.1");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task C2_A1_ConfiguredNetwork_LoopbackConnection_XffIgnored_LoopbackDefaultsCleared(string loopback)
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, loopback, Client);

        seen.Should().Be(loopback);
        ctx.Request.Headers["X-Forwarded-For"].ToString().Should().Be(Client);
    }

    [Fact]
    public async Task C7_C11_ProtoAndHostHeaders_SchemeAndHostUnchanged_NoIpInvented()
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, GatewayAddress, null, c =>
        {
            c.Request.Headers["X-Forwarded-Proto"] = "https";
            c.Request.Headers["X-Forwarded-Host"] = "evil.example.com";
        });

        seen.Should().Be(GatewayAddress);
        ctx.Request.Scheme.Should().Be("http");
        ctx.Request.Host.Value.Should().Be("iam.internal");
    }

    [Fact]
    public async Task C11_ProtoAndHostWithTrustedXff_OnlyClientIpChanges()
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, GatewayAddress, Client, c =>
        {
            c.Request.Headers["X-Forwarded-Proto"] = "https";
            c.Request.Headers["X-Forwarded-Host"] = "evil.example.com";
        });

        seen.Should().Be(Client);
        ctx.Request.Scheme.Should().Be("http");
        ctx.Request.Host.Value.Should().Be("iam.internal");
    }

    [Fact]
    public async Task C6_NullRemoteIp_NoUsableHeader_EmptyAndUnknownConventions_NoThrow()
    {
        var (ctx, seen) = await RunAsync(new[] { GatewayNetwork }, null, "not-an-ip");

        seen.Should().BeNull();
        AuthDomain().GetVisitorsIpAddresses(ctx).Should().BeEmpty();
        OidcRedirectUrlBuilder.GetClientIpAddress(ctx.Request).Should().Be("unknown");
    }

    // ---------- Multi-hop paths: ForwardLimit with bridge proxies ----------
    // Shapes observed on dev (pre-#594 refresh tokens): a bridge proxy behind the gateway re-sends to the
    // public API URL, so IAM receives "client, <gateway as seen by the bridge>, <cluster outbound>" and,
    // when the bridge reached the API through a CDN, "<CDN edge>" last. Documentation addresses stand in
    // for the outbound address and the CDN edge.

    private const string OutboundNetwork = "192.0.2.50/32";
    private const string OutboundAddress = "192.0.2.50";
    private const string EdgeNetwork = "192.0.2.128/25";
    private const string EdgeAddress = "192.0.2.130";
    private const string BridgedChain = Client + ", " + GatewayAddress + ", " + OutboundAddress;

    private static async Task<(DefaultHttpContext Ctx, string? Seen)> RunWithLimitAsync(string forwardLimit, string[] networks, string xff)
    {
        string? seen = "not-run";
        var pipeline = Build(ForwardedHeadersSetup.BuildOptions(ConfigWithForwardLimit(forwardLimit, networks)), c => seen = c.Connection.RemoteIpAddress?.ToString());
        var ctx = NewContext(GatewayAddress, xff);
        await pipeline(ctx);
        return (ctx, seen);
    }

    [Fact]
    public async Task MultiHop_BridgedThroughOutbound_WalksBackToClient()
    {
        var (ctx, seen) = await RunWithLimitAsync("3", new[] { GatewayNetwork, OutboundNetwork }, BridgedChain);

        seen.Should().Be(Client);
        AuthDomain().GetVisitorsIpAddresses(ctx).Should().Equal(Client);
    }

    [Fact]
    public async Task MultiHop_BridgedThroughOutboundAndCdnEdge_WalksBackToClient()
    {
        var (_, seen) = await RunWithLimitAsync("4", new[] { GatewayNetwork, OutboundNetwork, EdgeNetwork }, BridgedChain + ", " + EdgeAddress);

        seen.Should().Be(Client);
    }

    [Fact]
    public async Task MultiHop_EntriesTheClientWroteItself_AreNeverReached()
    {
        // The walk stops at the client (outside KnownNetworks), so a spare limit never reaches the
        // spoofed entry to its left.
        var (ctx, seen) = await RunWithLimitAsync("5", new[] { GatewayNetwork, OutboundNetwork }, Spoofed + ", " + BridgedChain);

        seen.Should().Be(Client);
        AuthDomain().GetVisitorsIpAddresses(ctx).Should().NotContain(Spoofed);
    }

    [Fact]
    public async Task MultiHop_DirectPath_LargerLimitChangesNothing()
    {
        var (_, seen) = await RunWithLimitAsync("4", new[] { GatewayNetwork, OutboundNetwork }, Client);

        seen.Should().Be(Client);
    }

    [Fact]
    public async Task MultiHop_OutboundNotTrusted_StopsAtOutbound()
    {
        var (_, seen) = await RunWithLimitAsync("4", new[] { GatewayNetwork }, BridgedChain);

        seen.Should().Be(OutboundAddress);
    }

    [Theory]
    [InlineData("1", OutboundAddress)]
    [InlineData("2", GatewayAddress)]
    public async Task MultiHop_LimitBelowHopCount_StopsShortOfClient(string forwardLimit, string expected)
    {
        var (_, seen) = await RunWithLimitAsync(forwardLimit, new[] { GatewayNetwork, OutboundNetwork }, BridgedChain);

        seen.Should().Be(expected);
    }

    [Fact]
    public async Task MultiHop_TrustedOutbound_BelievesWhatItForwards()
    {
        // The trade-off of trusting a shared outbound address: anything calling the public URL from
        // behind it can name the client. Only list outbound addresses used by our own proxies.
        var (_, seen) = await RunWithLimitAsync("4", new[] { GatewayNetwork, OutboundNetwork }, Spoofed + ", " + OutboundAddress);

        seen.Should().Be(Spoofed);
    }

    // ---------- Writers fed by a middleware-resolved request ----------

    private static AuthenticationDomainService AuthDomain() => new(
        Mock.Of<IMessageClient>(), Mock.Of<IAuthenticationRepository>(),
        new AuthenticationRequestValidators(
            Mock.Of<IValidator<SaveOIDCClientRequest>>(), Mock.Of<IValidator<SaveOidcUiTemplateRequest>>(),
            Mock.Of<IValidator<SaveIdentityProviderRequest>>(), Mock.Of<IValidator<UpdateIdentityProviderRequest>>()),
        Mock.Of<IHttpClientFactory>(), XUnitTest.TestDoubles.IdentityProviderSecretTestDoubles.Resolver(), XUnitTest.TestDoubles.ClientCredentialSecretTestDoubles.Resolver(), XUnitTest.TestDoubles.OidcClientSecretTestDoubles.Resolver());

    [Fact]
    public async Task H12_GetVisitorsIpAddresses_ReturnsOnlyTheResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync("198.51.100.99, " + Client);
        AuthDomain().GetVisitorsIpAddresses(ctx).Should().Equal(Client);
    }

    [Fact]
    public async Task H6_OidcRedirectUrlBuilder_GetClientIpAddress_ReturnsResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync(MultiEntry);
        var ip = OidcRedirectUrlBuilder.GetClientIpAddress(ctx.Request);
        ip.Should().Be(Client);
        ip.Should().NotContain(Spoofed);
    }

    [Fact]
    public async Task H7_MfaAuditService_RecordsResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync(MultiEntry);
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(ctx);
        var dispatcher = new Mock<IUserActivityDispatcher>();
        UserActivityEvent? captured = null;
        dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
            .Callback<UserActivityEvent>(e => captured = e).Returns(Task.CompletedTask);
        var domain = new Mock<IAuthenticationDomainService>();
        domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation());

        await new MfaAuditService(domain.Object, accessor.Object, dispatcher.Object, NullLogger<MfaAuditService>.Instance)
            .WriteAsync(new MfaAuditEvent { EventType = "mfa.verify", UserId = "u1", Status = "success" });

        captured!.Context.IpAddress.Should().Be(Client);
        captured.Context.IpAddress.Should().NotContain(Spoofed);
    }

    [Fact]
    public async Task H1_H8_IdpSessionService_ResolveOrCreate_StoresResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync(MultiEntry);
        var repo = new Mock<IIdpSessionRepository>();
        IdpSessionModel? stored = null;
        repo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>()))
            .Callback<IdpSessionModel>(m => stored = m).ReturnsAsync((IdpSessionModel m) => m.SessionId);

        // Every ResolveOrCreateAsync caller passes Connection.RemoteIpAddress?.ToString().
        await IdpSessions(repo).ResolveOrCreateAsync(ctx, "user-1", "tenant-1", ctx.Connection.RemoteIpAddress?.ToString());

        stored!.IpAddress.Should().Be(Client);
        stored.IpAddress.Should().NotContain(Spoofed);
    }

    private static IdpSessionModel ExistingSession(string sessionId, string ip) => new()
    {
        SessionId = sessionId,
        TenantId = "tenant-1",
        Accounts = new List<IdpSessionAccount> { new() { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow } },
        IpAddress = ip,
        CreatedAt = DateTime.UtcNow.AddHours(-1),
        LastActivityAt = DateTime.UtcNow,
        IdleExpiry = DateTime.UtcNow.AddHours(1),
        AbsoluteExpiry = DateTime.UtcNow.AddDays(30)
    };

    [Fact]
    public async Task C8_ExistingSession_ReusedViaCookie_KeepsHistoricalIp_NoNewSessionWithCurrentIp()
    {
        const string historical = "192.0.2.50";
        var ctx = await ThroughTrustedGatewayAsync(MultiEntry);
        ctx.Request.Headers["Cookie"] = $"{Iam.DomainService.Utilities.IdpConstants.BuildIdpSessionCookieKey("tenant-1")}=sess-old";
        var repo = new Mock<IIdpSessionRepository>();
        var existing = ExistingSession("sess-old", historical);
        repo.Setup(r => r.GetBySessionIdAsync("sess-old")).ReturnsAsync(existing);
        repo.Setup(r => r.UpdateActivityAsync("sess-old")).ReturnsAsync(true);

        var id = await IdpSessions(repo).ResolveOrCreateAsync(ctx, "user-1", "tenant-1", ctx.Connection.RemoteIpAddress?.ToString());

        id.Should().Be("sess-old");
        existing.IpAddress.Should().Be(historical);
        repo.Verify(r => r.CreateAsync(It.IsAny<IdpSessionModel>()), Times.Never);
    }

    [Fact]
    public async Task C8_ExistingSession_Rotated_KeepsHistoricalIp_NotTheCurrentRequestIp()
    {
        const string historical = "192.0.2.50";
        var ctx = await ThroughTrustedGatewayAsync(MultiEntry);
        ctx.Connection.RemoteIpAddress!.ToString().Should().Be(Client, "the current request resolves a different client");
        var repo = new Mock<IIdpSessionRepository>();
        repo.Setup(r => r.GetBySessionIdAsync("sess-old")).ReturnsAsync(ExistingSession("sess-old", historical));
        IdpSessionModel? rotated = null;
        repo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>()))
            .Callback<IdpSessionModel>(m => rotated = m).ReturnsAsync((IdpSessionModel m) => m.SessionId);

        var newId = await IdpSessions(repo).RotateSessionAsync("sess-old", "login");

        newId.Should().NotBeNull().And.NotBe("sess-old");
        rotated!.IpAddress.Should().Be(historical);
        rotated.IpAddress.Should().NotBe(Client);
    }

    private static Authentication.DomainService.Oidc.Services.IdpSessionService IdpSessions(Mock<IIdpSessionRepository> repo) => new(
        repo.Object, Mock.Of<IAuthenticationDomainService>(), Mock.Of<IRefreshTokenRepository>(), Mock.Of<ICacheClient>(),
        Mock.Of<IUserActivityDispatcher>(), Mock.Of<ITenants>(),
        NullLogger<Authentication.DomainService.Oidc.Services.IdpSessionService>.Instance);

    private sealed class TokenFixture
    {
        public readonly Mock<ICacheClient> Cache = new();
        public readonly Mock<IRefreshTokenRepository> RefreshRepo = new();
        public readonly Mock<IIdpSessionRepository> SessionRepo = new();
        public readonly Mock<IHttpContextAccessor> Accessor = new();
        public readonly Mock<IUserRepository> Users = new();
        public RefreshTokenModel? StoredRefreshToken;
        public IdpSessionModel? StoredSession;

        public TokenFixture(HttpContext ctx)
        {
            Accessor.Setup(a => a.HttpContext).Returns(ctx);
            Cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            RefreshRepo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()))
                .Callback<RefreshTokenModel>(m => StoredRefreshToken = m).ReturnsAsync("id");
            SessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>()))
                .Callback<IdpSessionModel>(m => StoredSession = m).ReturnsAsync((IdpSessionModel m) => m.SessionId);
            Users.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "u1" });
        }

        public IIdpSessionService Sessions => IdpSessions(SessionRepo);

        public UnifiedTokenSessionService Unified(IAuthenticationDomainService domain) => new(
            Cache.Object, domain, RefreshRepo.Object, Mock.Of<IUserActivityDispatcher>(), Sessions,
            Accessor.Object, Users.Object, NullLogger<UnifiedTokenSessionService>.Instance);
    }

    [Fact]
    public async Task H4_H11_Example7_OAuthGrant_SessionAndRefreshTokenRecordTheSameResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync("198.51.100.99, " + Client);
        var f = new TokenFixture(ctx);
        var domain = AuthDomain(); // real GetVisitorsIpAddresses
        var manager = new OAuthJwtAccessTokenManager(
            Mock.Of<IJwtAccessTokenProvider>(), domain, Mock.Of<IAuthenticationRepository>(),
            Mock.Of<IMfaPolicyService>(), f.Cache.Object, Mock.Of<ITenants>(),
            Mock.Of<IOtpServiceFactory>(), f.Unified(domain), f.Sessions);

        var request = new TokenRequest { GrantType = GrantTypes.Password, ClientId = "client-1", OrganizationId = "default", Request = ctx.Request };
        await manager.ManageRefreshTokenAsync(
            request,
            XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
            new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 30, AbsoluteRefreshTokenValidForNumberMinutes = 10080 },
            XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeTenant(),
            new User { ItemId = "u1" });

        f.StoredSession!.IpAddress.Should().Be(Client);
        f.StoredRefreshToken!.IpAddress.Should().Be(Client, "a single address, the resolved client (H11)");
    }

    [Fact]
    public async Task H11_AuthorizationCodeRefreshToken_OidcTokenGeneration_RecordsResolvedClient()
    {
        var ctx = await ThroughTrustedGatewayAsync(Client);
        var f = new TokenFixture(ctx);
        var tenants = new Mock<ITenants>();
        tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeTenant());
        var authRepo = new Mock<IAuthenticationRepository>();
        authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
            .ReturnsAsync(new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 30, AbsoluteRefreshTokenValidForNumberMinutes = 10080 });
        var service = new TokenGenerationService(
            new OidcSigningKeyMaterial(), tenants.Object, f.Cache.Object, f.Unified(AuthDomain()),
            Mock.Of<ICryptoService>(), Mock.Of<ICertificateProviderFactory>(), f.Accessor.Object, authRepo.Object);

        var model = await service.GenerateRefreshTokenAsync(
            new OidcClaims { Sub = "u1", TenantId = "tenant-1", ClientId = "client-1", Scope = "openid" },
            "https://issuer", isImpersonation: false, idpSessionId: "sess-1");

        model.IpAddress.Should().Be(Client);
        f.StoredRefreshToken!.IpAddress.Should().Be(Client);
    }

    // ---------- Code grant redeemed by a server: the token records the browser that signed in ----------

    private const string ChromeOnWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36";

    private static TokenGenerationService CodeGrantTokenService(TokenFixture f)
    {
        var tenants = new Mock<ITenants>();
        tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeTenant());
        var authRepo = new Mock<IAuthenticationRepository>();
        authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
            .ReturnsAsync(new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 30, AbsoluteRefreshTokenValidForNumberMinutes = 10080 });
        return new TokenGenerationService(
            new OidcSigningKeyMaterial(), tenants.Object, f.Cache.Object, f.Unified(AuthDomain()),
            Mock.Of<ICryptoService>(), Mock.Of<ICertificateProviderFactory>(), f.Accessor.Object, authRepo.Object);
    }

    [Fact]
    public async Task CodeGrantRefreshToken_RedeemedByAServer_RecordsTheSignInBrowser()
    {
        // The redeeming request comes from the server (the outbound address); the claims carry the
        // browser that signed in, as stored on the authorization code.
        var ctx = await ThroughTrustedGatewayAsync(OutboundAddress);
        var f = new TokenFixture(ctx);

        var model = await CodeGrantTokenService(f).GenerateRefreshTokenAsync(
            new OidcClaims
            {
                Sub = "u1", TenantId = "tenant-1", ClientId = "client-1", Scope = "openid",
                SignInIpAddress = Client, SignInUserAgent = ChromeOnWindows
            },
            "https://issuer", isImpersonation: false, idpSessionId: "sess-1");

        model.IpAddress.Should().Be(Client);
        model.UserAgent.Should().Be(ChromeOnWindows);
        f.StoredRefreshToken!.IpAddress.Should().Be(Client);
        f.StoredRefreshToken.UserAgent.Should().Be(ChromeOnWindows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task CodeGrantRefreshToken_WithoutASignInAddress_RecordsTheRequest(string? signInIpAddress)
    {
        var ctx = await ThroughTrustedGatewayAsync(Client);
        var f = new TokenFixture(ctx);

        var model = await CodeGrantTokenService(f).GenerateRefreshTokenAsync(
            new OidcClaims { Sub = "u1", TenantId = "tenant-1", ClientId = "client-1", Scope = "openid", SignInIpAddress = signInIpAddress },
            "https://issuer", isImpersonation: false, idpSessionId: "sess-1");

        model.IpAddress.Should().Be(Client);
        f.StoredRefreshToken!.IpAddress.Should().Be(Client);
    }

    // ---------- Section 5: performance contract ----------

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Perf_ForwardedHeadersMiddleware_P95OverheadWithinBudget()
    {
        const int iterations = 10_000;
        var baseline = Build(null);
        var withMiddleware = Build(ForwardedHeadersSetup.BuildOptions(Config(GatewayNetwork)));

        async Task<double> P95Async(RequestDelegate pipeline)
        {
            var samples = new double[iterations];
            for (var i = 0; i < 1_000; i++) await pipeline(NewContext(GatewayAddress, Client)); // warm-up
            for (var i = 0; i < iterations; i++)
            {
                var ctx = NewContext(GatewayAddress, Client);
                var start = Stopwatch.GetTimestamp();
                await pipeline(ctx);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            Array.Sort(samples);
            return samples[(int)(iterations * 0.95)];
        }

        var before = await P95Async(baseline);
        var after = await P95Async(withMiddleware);
        _output.WriteLine($"p95 per request over {iterations}: before (next only) = {before * 1000:F2} us, after (forwarded headers) = {after * 1000:F2} us, delta = {(after - before) * 1000:F2} us");

        // Generous: the spec budget is <= 1 ms p95 regression; not a tight CI threshold.
        (after - before).Should().BeLessThan(1.0);
    }
}
