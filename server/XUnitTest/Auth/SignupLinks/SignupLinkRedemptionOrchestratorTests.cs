using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Services;
using Authentication.DomainService.SignupLinks;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace XUnitTest.Auth.SignupLinks;

public class SignupLinkRedemptionOrchestratorTests : IDisposable
{
    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<ISignupLinkRedemptionRepository> _redemptions = new();
    private readonly Mock<ILinkSessionRepository> _sessions = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();
    private readonly Mock<IAuthenticationRepository> _authentication = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserManagementMutationService> _mutation = new();
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<ICacheClient> _cache = new();
    private readonly Mock<ITenants> _tenants = new();
    private readonly Mock<IMfaChallengeIssuer> _mfa = new();
    private readonly Mock<ISignupLinkEmbeddedTokenIssuer> _embeddedTokens = new();
    private readonly Mock<IUserActivityDispatcher> _activity = new();

    public SignupLinkRedemptionOrchestratorTests()
    {
        BlocksContext.IsTestMode = true;
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: "t1", roles: null, userId: "x", impersonated: false,
            isAuthenticated: false, requestUri: "https://test", organizationId: "org-acme",
            permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
            userName: null, phoneNumber: null, displayName: null, oauthToken: null,
            originalTenantId: "t1", impersonationSessionId: null, applicationDomain: "test"));
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null!);
        BlocksContext.IsTestMode = false;
        GC.SuppressFinalize(this);
    }

    private SignupLinkRedemptionOrchestrator Sut()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BLOCKS_IAM_BASE_URL"] = "https://iam.example.com"
            }).Build();

        _tenants.Setup(t => t.GetTenantByID("t1")).Returns(new Tenant
        {
            TenantId = "t1",
            DbConnectionString = "",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
        });
        DomainResolver.Configure(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        _redemptions.Setup(r => r.InsertAsync(It.IsAny<SignupLinkRedemption>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<LinkSessionModel>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 60 });
        _users.Setup(u => u.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo(
                "construct-web",
                ["https://construct.example.com/callback"],
                true,
                "Construct"));

        // The IdentityProvider mirroring the client, as RegisterAsIdentityProvider writes it:
        // Provider is the slugged display name, never the provider *type*.
        _authentication.Setup(a => a.GetIdentityProviderByClientIdAsync("construct-web"))
            .ReturnsAsync(Provider());

        var stores = new SignupLinkRedemptionStores(
            _links.Object,
            _redemptions.Object,
            _sessions.Object,
            _users.Object,
            _iam.Object);
        var collaborators = new SignupLinkRedemptionCollaborators(
            _oidc.Object,
            _authentication.Object,
            _mutation.Object,
            _cache.Object,
            _tenants.Object,
            config,
            _mfa.Object,
            _embeddedTokens.Object,
            _activity.Object,
            NullLogger<SignupLinkRedemptionCollaborators>.Instance);
        return new SignupLinkRedemptionOrchestrator(stores, collaborators);
    }

    private static Authentication.DomainService.Entities.IdentityProvider Provider(bool requirePkce = false) => new()
    {
        Provider = "construct",
        ProviderType = Iam.DomainService.Utilities.IdpConstants.BlocksOidcProviderType,
        ClientId = "construct-web",
        ClientSecret = "shhh",
        TokenEndpointAuthMethod = "client_secret_post",
        IsActive = true,
        RequirePkce = requirePkce
    };

    private static SignupLink ActivePasswordless(string email = "new@example.com") => new()
    {
        ItemId = "link-1",
        TenantId = "t1",
        OrganizationId = "org-acme",
        CodeHash = SignupLinkCodeHasher.Hash("good-code"),
        Email = email,
        FirstName = "Asif",
        LastName = "R",
        ClientId = "construct-web",
        RedirectUri = "https://construct.example.com/callback",
        ForwardedTo = "/projects",
        CredentialMode = SignupLinkCredentialMode.Passwordless,
        Roles = ["site-manager"],
        Permissions = ["perm.a"],
        Status = SignupLinkStatus.Active,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        MaxRedemptions = 1,
        RedemptionCount = 0
    };

    private static DefaultHttpContext Http()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.UserAgent = "test-agent";
        return ctx;
    }

    private void SetupNewUserCreate(string userId, string email)
    {
        var mapped = new User { ItemId = userId, Email = email };
        _mutation.Setup(m => m.MapUser(It.IsAny<CreateUserRequest>())).Returns(mapped);
        _users.Setup(u => u.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        _users.Setup(u => u.GetUserByIdAsync(userId)).ReturnsAsync(mapped);
    }

    [Fact]
    public async Task Redeem_HappyPath_CreatesUser_SetsCookie_ReturnsAuthorizeUrl()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        SetupNewUserCreate("user-new", link.Email);

        var redeemed = ActivePasswordless();
        redeemed.RedemptionCount = 1;
        redeemed.Status = SignupLinkStatus.Redeemed;
        redeemed.CreatedUserId = "user-new";
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "user-new", It.IsAny<DateTime>()))
            .ReturnsAsync(redeemed);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.AuthorizeUrl.Should().StartWith("https://iam.example.com/api/oidc/authorize?");
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.UserCreated && x.UserId == "user-new")), Times.Once);
    }

    /// <summary>
    /// Sets up a redeemable link and returns the cached flow context the redemption wrote,
    /// which is what <c>GET /idp/callback</c> will later read back.
    /// </summary>
    private async Task<System.Text.Json.JsonElement> RedeemAndCaptureFlowContextAsync()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        SetupNewUserCreate("user-new", link.Email);

        var redeemed = ActivePasswordless();
        redeemed.RedemptionCount = 1;
        redeemed.Status = SignupLinkStatus.Redeemed;
        redeemed.CreatedUserId = "user-new";
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "user-new", It.IsAny<DateTime>()))
            .ReturnsAsync(redeemed);

        // After Sut(), which registers the catch-all AddStringValueAsync setup this one
        // narrows -- in Moq the last matching setup wins.
        var sut = Sut();
        string? captured = null;
        _cache.Setup(c => c.AddStringValueAsync(
                It.Is<string>(k => k.StartsWith("idp_flow:", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<long>()))
            .Callback<string, string, long>((_, value, _) => captured = value)
            .ReturnsAsync(true);

        var http = Http();
        var result = await sut.RedeemAsync("good-code", "t1", http.Request, http.Response);

        var body = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.AuthorizeUrl.Should().NotBeNullOrEmpty();

        captured.Should().NotBeNull();
        return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(captured!);
    }

    [Fact]
    public async Task Redeem_CachesFlowContext_UnderTheClientsOwnProviderName()
    {
        var context = await RedeemAndCaptureFlowContextAsync();

        // /idp/callback resolves this by Provider name against the IdentityProvider
        // collection. Writing a provider *type* here -- "blocks", or even "blocks-oidc" --
        // matches no document, and the construct's exchange fails with invalid_provider
        // after the account has already been created and the code burned.
        context.GetProperty("provider").GetString().Should().Be("construct");
    }

    [Fact]
    public async Task Redeem_ProviderNotRegisteredForClient_IsRejectedBeforeAnyUserIsCreated()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);

        var sut = Sut();
        _authentication.Setup(a => a.GetIdentityProviderByClientIdAsync("construct-web"))
            .ReturnsAsync((Authentication.DomainService.Entities.IdentityProvider)null!);

        var http = Http();
        var result = await sut.RedeemAsync("good-code", "t1", http.Request, http.Response);

        result.Should().BeOfType<BadRequestObjectResult>();
        _mutation.Verify(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.Rejected)), Times.Once);
    }

    [Fact]
    public async Task Redeem_InactiveProvider_IsRejected()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);

        var inactive = Provider();
        inactive.IsActive = false;
        var sut = Sut();
        _authentication.Setup(a => a.GetIdentityProviderByClientIdAsync("construct-web"))
            .ReturnsAsync(inactive);

        var http = Http();
        var result = await sut.RedeemAsync("good-code", "t1", http.Request, http.Response);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Redeem_ProviderWithoutPkce_SendsNoChallengeAndCachesNoVerifier()
    {
        var context = await RedeemAndCaptureFlowContextAsync();

        // The two halves have to agree: /idp/callback sends code_verifier only when the
        // context holds one, and the token endpoint checks it only when the authorize request
        // stored a challenge. Half a pair is an invalid_grant at the very last step.
        context.GetProperty("codeVerifier").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Redeem_ProviderRequiringPkce_CachesVerifierAndSendsMatchingChallenge()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        SetupNewUserCreate("user-new", link.Email);

        var redeemed = ActivePasswordless();
        redeemed.RedemptionCount = 1;
        redeemed.Status = SignupLinkStatus.Redeemed;
        redeemed.CreatedUserId = "user-new";
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "user-new", It.IsAny<DateTime>()))
            .ReturnsAsync(redeemed);

        var sut = Sut();
        _authentication.Setup(a => a.GetIdentityProviderByClientIdAsync("construct-web"))
            .ReturnsAsync(Provider(requirePkce: true));

        string? captured = null;
        _cache.Setup(c => c.AddStringValueAsync(
                It.Is<string>(k => k.StartsWith("idp_flow:", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<long>()))
            .Callback<string, string, long>((_, value, _) => captured = value)
            .ReturnsAsync(true);

        var http = Http();
        var result = await sut.RedeemAsync("good-code", "t1", http.Request, http.Response);

        var url = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject.AuthorizeUrl;

        var verifier = System.Text.Json.JsonSerializer
            .Deserialize<System.Text.Json.JsonElement>(captured!)
            .GetProperty("codeVerifier").GetString();
        verifier.Should().NotBeNullOrEmpty();

        var expected = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        url.Should().Contain($"code_challenge={Uri.EscapeDataString(expected)}");
        url.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task H1_LinkUserReturned_IssuesSession_NoGrant()
    {
        var link = ActivePasswordless("linkuser@example.com");
        link.Status = SignupLinkStatus.Redeemed;
        link.RedemptionCount = 1;
        link.CreatedUserId = "user-link";
        link.MaxRedemptions = 2;
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var user = new User
        {
            ItemId = "user-link",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active,
            OrganizationIds = ["org-acme"],
            Roles = new Dictionary<string, List<string>> { ["org-acme"] = ["site-manager"] }
        };
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.AuthorizeUrl.Should().NotBeNullOrEmpty();
        body.LoginUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.LinkUserReturned)), Times.Once);
    }

    [Fact]
    public async Task H2_OrganizationJoined_GrantsOrgOnly_AndLandsSignedIn()
    {
        var link = ActivePasswordless("existing@example.com");
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var user = new User
        {
            ItemId = "user-existing",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active,
            OrganizationIds = ["org-other"],
            Roles = new Dictionary<string, List<string>> { ["org-other"] = ["admin"] },
            Permissions = new Dictionary<string, List<string>> { ["org-other"] = ["p1"] }
        };
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);
        _users.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var consumed = ActivePasswordless(link.Email);
        consumed.RedemptionCount = 1;
        consumed.Status = SignupLinkStatus.Redeemed;
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()))
            .ReturnsAsync(consumed);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;

        // The link exists to put someone on a page. An existing account now finishes the same
        // way a new one does -- it used to be handed a login URL and left to sign in.
        body.AuthorizeUrl.Should().StartWith("https://iam.example.com/api/oidc/authorize?");
        body.LoginUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");

        user.OrganizationIds.Should().Contain("org-acme");
        user.Roles["org-acme"].Should().Equal("site-manager");
        user.Roles["org-other"].Should().Equal("admin");
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.OrganizationJoined)), Times.Once);
    }

    [Fact]
    public async Task C1_ExistingUserRedirected_GrantsNothing_ButStillLandsSignedIn()
    {
        var link = ActivePasswordless("member@example.com");
        link.Roles = ["tenant-admin"];
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var user = new User
        {
            ItemId = "user-member",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active,
            OrganizationIds = ["org-acme"],
            Roles = new Dictionary<string, List<string>> { ["org-acme"] = ["site-manager"] },
            Permissions = new Dictionary<string, List<string>>()
        };
        var rolesBefore = user.Roles["org-acme"].ToList();
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);

        var consumed = ActivePasswordless(link.Email);
        consumed.RedemptionCount = 1;
        consumed.Status = SignupLinkStatus.Redeemed;
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()))
            .ReturnsAsync(consumed);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;

        // Already a member, so nothing is granted -- but they still reach the page, which is
        // what the link was for.
        body.AuthorizeUrl.Should().NotBeNullOrEmpty();
        body.LoginUrl.Should().BeNull();
        user.Roles["org-acme"].Should().Equal(rolesBefore);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.ExistingUserRedirected)), Times.Once);
    }

    private User ExistingMember(bool mfaEnabled = false) => new()
    {
        ItemId = "user-member",
        Email = "member@example.com",
        Active = true,
        Status = UserLifecycleStatus.Active,
        OrganizationIds = ["org-acme"],
        Roles = new Dictionary<string, List<string>> { ["org-acme"] = ["site-manager"] },
        Permissions = new Dictionary<string, List<string>>(),
        MfaEnabled = mfaEnabled,
        UserMfaType = UserMfaType.Email
    };

    private void SetupExistingRedemption(SignupLink link, User user)
    {
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);
        _users.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

        var consumed = ActivePasswordless(link.Email);
        consumed.RedemptionCount = 1;
        consumed.Status = SignupLinkStatus.Redeemed;
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()))
            .ReturnsAsync(consumed);
    }

    [Fact]
    public async Task ExistingUser_WithMfa_IsChallenged_NotSignedInByTheLinkAlone()
    {
        var link = ActivePasswordless("member@example.com");
        var user = ExistingMember(mfaEnabled: true);
        SetupExistingRedemption(link, user);

        var otp = new Mock<IOtpService>();
        otp.Setup(o => o.GenerateAsync(It.IsAny<global::Mfa.DomainService.Entities.UserInfo>()))
            .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-7" });
        _mfa.Setup(m => m.GetOtpServiceAsync(user)).ReturnsAsync(otp.Object);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;

        // This branch never issued a session before, so it never needed an MFA check. Wiring
        // one in without this would let an emailed link walk past a second factor.
        body.MfaId.Should().Be("mfa-7");
        body.AuthorizeUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
    }

    [Fact]
    public async Task ExistingUser_Embedded_IssuesTokensCarryingTheForwardedPath()
    {
        var link = ActivePasswordless("member@example.com");
        link.Mode = SignupLinkMode.Embedded;
        link.ClientId = string.Empty;
        link.RedirectUri = string.Empty;
        link.ForwardedTo = "/projects";
        SetupExistingRedemption(link, ExistingMember());

        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(new OkObjectResult(new Dictionary<string, object?> { ["token_type"] = "Bearer" }));

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);

        // Embedded has no redirect to carry the path, so the token body has to.
        var payload = result.Should().BeOfType<ObjectResult>().Subject
            .Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload["forwardedTo"].Should().Be("/projects");
    }

    [Fact]
    public async Task NewUser_Embedded_AlsoCarriesTheForwardedPath()
    {
        var link = ActivePasswordless("brandnew@example.com");
        link.Mode = SignupLinkMode.Embedded;
        link.ClientId = string.Empty;
        link.RedirectUri = string.Empty;
        link.ForwardedTo = "/projects";
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        SetupNewUserCreate("user-new", link.Email);

        var redeemed = ActivePasswordless(link.Email);
        redeemed.RedemptionCount = 1;
        redeemed.Status = SignupLinkStatus.Redeemed;
        redeemed.CreatedUserId = "user-new";
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "user-new", It.IsAny<DateTime>()))
            .ReturnsAsync(redeemed);

        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(new OkObjectResult(new Dictionary<string, object?> { ["token_type"] = "Bearer" }));

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);

        // The gap that shipped with embedded mode: a brand-new invitee signed in and landed
        // wherever the app defaulted to, never on the page the link named.
        var payload = result.Should().BeOfType<ObjectResult>().Subject
            .Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload["forwardedTo"].Should().Be("/projects");
    }

    [Fact]
    public async Task H3_PasswordRequired_CreatesPending_ReturnsActivationKey()
    {
        var link = ActivePasswordless("new2@example.com");
        link.CredentialMode = SignupLinkCredentialMode.PasswordRequired;
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);

        User? created = null;
        _mutation.Setup(m => m.MapUser(It.IsAny<CreateUserRequest>())).Returns<CreateUserRequest>(r =>
        {
            created = new User { ItemId = "user-pending", Email = r.Email };
            return created;
        });
        _users.Setup(u => u.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        _users.Setup(u => u.GetUserByIdAsync("user-pending")).ReturnsAsync(() => created!);

        var redeemed = ActivePasswordless(link.Email);
        redeemed.CredentialMode = SignupLinkCredentialMode.PasswordRequired;
        redeemed.RedemptionCount = 1;
        redeemed.Status = SignupLinkStatus.Redeemed;
        redeemed.CreatedUserId = "user-pending";
        _links.Setup(l => l.TryIncrementRedemptionAsync("link-1", "t1", "user-pending", It.IsAny<DateTime>()))
            .ReturnsAsync(redeemed);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.ActivationKey.Should().NotBeNullOrEmpty();
        body.CredentialMode.Should().Be("PasswordRequired");
        body.AuthorizeUrl.Should().BeNull();
        created!.Status.Should().Be(UserLifecycleStatus.PendingVerification);
        created.Active.Should().BeFalse();
        _users.Verify(u => u.InsertUserKeyMapAsync(It.Is<UserKeyMap>(k =>
            k.Value == "signup-link:link-1" && k.MailPurpose == "SignupLink")), Times.Once);
    }

    [Fact]
    public async Task H3_PasswordRequired_Resume_ReturnsNewActivationKey()
    {
        var link = ActivePasswordless("new2@example.com");
        link.CredentialMode = SignupLinkCredentialMode.PasswordRequired;
        link.Status = SignupLinkStatus.Redeemed;
        link.RedemptionCount = 1;
        link.CreatedUserId = "user-pending";
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var user = new User
        {
            ItemId = "user-pending",
            Email = link.Email,
            Active = false,
            Status = UserLifecycleStatus.PendingVerification
        };
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.ActivationKey.Should().NotBeNullOrEmpty();
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.LinkUserReturned)), Times.Once);
    }

    [Fact]
    public async Task C5_LockedAccount_ReturnsInvalidLink()
    {
        var link = ActivePasswordless("locked@example.com");
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(new User
        {
            ItemId = "user-locked",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active,
            LockoutUntilUtc = DateTime.UtcNow.AddHours(1)
        });

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>()
            .Which.Error.Should().Be("invalid_link");
    }

    /// <summary>
    /// An invitee who never clicked the invite email: pending, inactive, unverified, and holding
    /// an outstanding activation key from that email.
    /// </summary>
    private User PendingInvitee(params string[] organizationIds)
    {
        var user = new User
        {
            ItemId = "user-invited",
            Email = "invited@example.com",
            Active = false,
            IsVerified = false,
            Status = UserLifecycleStatus.PendingVerification,
            OrganizationIds = organizationIds.ToList(),
            Roles = organizationIds.ToDictionary(o => o, _ => new List<string> { "member" }),
            Permissions = organizationIds.ToDictionary(o => o, _ => new List<string>())
        };
        _iam.Setup(i => i.GetActiveUserKeyMapAsync(user.ItemId))
            .ReturnsAsync([new UserKeyMap { Key = "invite-key", UserId = user.ItemId }]);
        _iam.Setup(i => i.UpdateUserKeyMapActivationAsync(user.ItemId)).ReturnsAsync(true);
        _cache.Setup(c => c.RemoveKeyAsync("invite-key")).ReturnsAsync(true);
        return user;
    }

    [Fact]
    public async Task PendingInvitee_PasswordlessLink_IsActivated_JoinsOrg_AndLandsSignedIn()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-other");
        SetupExistingRedemption(link, user);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;

        // This used to be invalid_link: the link was refused because the invite was never
        // accepted, leaving the activation email as the only way in.
        body.AuthorizeUrl.Should().StartWith("https://iam.example.com/api/oidc/authorize?");
        user.Active.Should().BeTrue();
        user.IsVerified.Should().BeTrue();
        user.Status.Should().Be(UserLifecycleStatus.Active);
        user.EmailVerifiedAtUtc.Should().NotBeNull();
        user.OrganizationIds.Should().Contain(["org-other", "org-acme"]);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.OrganizationJoined)), Times.Once);
    }

    [Fact]
    public async Task PendingInvitee_Activation_RetiresTheInviteKey_AndRecordsTheActivation()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-other");
        SetupExistingRedemption(link, user);

        await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);

        // Otherwise the invite email would still set a password on an account that is active.
        _cache.Verify(c => c.RemoveKeyAsync("invite-key"), Times.Once);
        _iam.Verify(i => i.UpdateUserKeyMapActivationAsync("user-invited"), Times.Once);
        _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e =>
            e.UserId == "user-invited" && e.Event == "Activate_Account" && e.Source == "signup-link")), Times.Once);
    }

    [Fact]
    public async Task PendingInvitee_AlreadyInTheLinksOrg_IsActivated_WithoutAGrant()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-acme");
        SetupExistingRedemption(link, user);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        user.Status.Should().Be(UserLifecycleStatus.Active);
        user.Roles["org-acme"].Should().Equal("member");
        _users.Verify(u => u.UpdateUserAsync(user), Times.Once);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.ExistingUserRedirected)), Times.Once);
    }

    [Fact]
    public async Task PendingInvitee_PasswordRequiredLink_ReturnsActivationKey_AndStaysPending()
    {
        var link = ActivePasswordless("invited@example.com");
        link.CredentialMode = SignupLinkCredentialMode.PasswordRequired;
        var user = PendingInvitee("org-other");
        SetupExistingRedemption(link, user);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;

        // The account turns active when the password is set, through the activation endpoint,
        // which retires every key itself -- nothing is activated or retired here.
        body.ActivationKey.Should().NotBeNullOrEmpty();
        body.CredentialMode.Should().Be("PasswordRequired");
        body.AuthorizeUrl.Should().BeNull();
        user.Status.Should().Be(UserLifecycleStatus.PendingVerification);
        user.OrganizationIds.Should().Contain("org-acme");
        _users.Verify(u => u.InsertUserKeyMapAsync(It.Is<UserKeyMap>(k =>
            k.UserId == "user-invited" && k.Value == "signup-link:link-1")), Times.Once);
        _links.Verify(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()), Times.Once);
        _iam.Verify(i => i.UpdateUserKeyMapActivationAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PendingInvitee_WithAnAuthenticatorNeverEnrolled_IsNotChallenged()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-other");
        user.MfaEnabled = true;
        user.UserMfaType = UserMfaType.TOTP;
        user.IsMfaVerified = false;
        SetupExistingRedemption(link, user);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);

        // A challenge against an authenticator that was never set up could only fail.
        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        _mfa.Verify(m => m.GetOtpServiceAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task PendingInvitee_WithEmailMfa_IsStillChallenged()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-other");
        user.MfaEnabled = true;
        user.UserMfaType = UserMfaType.Email;
        SetupExistingRedemption(link, user);

        var otp = new Mock<IOtpService>();
        otp.Setup(o => o.GenerateAsync(It.IsAny<global::Mfa.DomainService.Entities.UserInfo>()))
            .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-9" });
        _mfa.Setup(m => m.GetOtpServiceAsync(user)).ReturnsAsync(otp.Object);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.MfaId.Should().Be("mfa-9");
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
    }

    [Fact]
    public async Task PendingInvitee_LockedOut_IsStillRejected()
    {
        var link = ActivePasswordless("invited@example.com");
        var user = PendingInvitee("org-other");
        user.LockoutUntilUtc = DateTime.UtcNow.AddHours(1);
        SetupExistingRedemption(link, user);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>()
            .Which.Error.Should().Be("invalid_link");
        user.Status.Should().Be(UserLifecycleStatus.PendingVerification);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task H4_MfaEnrolled_ReturnsChallenge_NoCookie()
    {
        var link = ActivePasswordless("mfa@example.com");
        link.Status = SignupLinkStatus.Redeemed;
        link.RedemptionCount = 1;
        link.CreatedUserId = "user-mfa";
        link.MaxRedemptions = 2;
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var user = new User
        {
            ItemId = "user-mfa",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active,
            MfaEnabled = true,
            UserMfaType = UserMfaType.TOTP
        };
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);

        var otp = new Mock<IOtpService>();
        otp.Setup(o => o.GenerateAsync(It.IsAny<global::Mfa.DomainService.Entities.UserInfo>()))
            .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-1" });
        _mfa.Setup(m => m.GetOtpServiceAsync(user)).ReturnsAsync(otp.Object);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.MfaId.Should().Be("mfa-1");
        body.Error.Should().NotBeNullOrEmpty();
        body.AuthorizeUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
    }

    [Fact]
    public async Task C4_TryBind_OrdinaryInvite_SetsNoCookie()
    {
        _iam.Setup(i => i.GetUserKeyMapByKeyAsync("invite-key"))
            .ReturnsAsync(new UserKeyMap { Key = "invite-key", Value = "https://iam/activate?code=x", UserId = "u1" });

        var http = Http();
        var result = await Sut().CompleteActivationAsync("invite-key", http.Request, http.Response);

        // An ordinary invite stores a URL in Value, so it can never match the signup-link
        // prefix. Null means the controller returns the plain activation response, unchanged.
        result.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
        _sessions.Verify(s => s.CreateAsync(It.IsAny<LinkSessionModel>()), Times.Never);
    }

    [Fact]
    public async Task H3_TryBind_SignupLinkKey_SetsCookie()
    {
        _iam.Setup(i => i.GetUserKeyMapByKeyAsync("act-key"))
            .ReturnsAsync(new UserKeyMap
            {
                Key = "act-key",
                Value = "signup-link:link-1",
                UserId = "user-pending"
            });
        _users.Setup(u => u.GetUserByIdAsync("user-pending")).ReturnsAsync(new User
        {
            ItemId = "user-pending",
            Email = "new2@example.com",
            Active = true,
            Status = UserLifecycleStatus.Active
        });
        _links.Setup(l => l.GetByItemIdAsync("link-1")).ReturnsAsync(ActivePasswordless("new2@example.com"));

        var http = Http();
        var result = await Sut().CompleteActivationAsync("act-key", http.Request, http.Response);

        // SignInAfterActivation off: today's behaviour, the invitee signs in afterwards.
        result.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _sessions.Verify(s => s.CreateAsync(It.Is<LinkSessionModel>(m =>
            m.UserId == "user-pending" && m.Amr.Contains("link"))), Times.Once);
    }

    /// <summary>Wires an activation key through to the given link.</summary>
    private void SetupActivation(SignupLink link)
    {
        _iam.Setup(i => i.GetUserKeyMapByKeyAsync("act-key"))
            .ReturnsAsync(new UserKeyMap { Key = "act-key", Value = "signup-link:link-1", UserId = "user-pending" });
        _users.Setup(u => u.GetUserByIdAsync("user-pending")).ReturnsAsync(new User
        {
            ItemId = "user-pending",
            Email = link.Email,
            Active = true,
            Status = UserLifecycleStatus.Active
        });
        _links.Setup(l => l.GetByItemIdAsync("link-1")).ReturnsAsync(link);
    }

    private static SignupLink SignInOnActivation(SignupLinkMode mode)
    {
        var link = ActivePasswordless("pwd@example.com");
        link.CredentialMode = SignupLinkCredentialMode.PasswordRequired;
        link.SignInAfterActivation = true;
        link.Mode = mode;
        link.ForwardedTo = "/projects";
        if (mode == SignupLinkMode.Embedded)
        {
            link.ClientId = string.Empty;
            link.RedirectUri = string.Empty;
        }
        return link;
    }

    [Theory]
    [InlineData(SignupLinkMode.Oidc)]
    [InlineData(SignupLinkMode.Embedded)]
    public async Task Activation_SignInAfterActivation_IssuesTokens_ForBothModes(SignupLinkMode mode)
    {
        SetupActivation(SignInOnActivation(mode));
        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(new OkObjectResult(new Dictionary<string, object?> { ["token_type"] = "Bearer" }));

        var http = Http();
        var result = await Sut().CompleteActivationAsync("act-key", http.Request, http.Response);

        // Both modes take the same path: the issuer needs no client, which is exactly why the
        // link-session cookie could never have served embedded.
        result.Should().NotBeNull();
        _embeddedTokens.Verify(t => t.IssueAsync(
            It.Is<SignupLink>(l => l.ItemId == "link-1"),
            It.Is<User>(u => u.ItemId == "user-pending"),
            It.Is<List<string>>(a => a.Contains("link")),
            It.IsAny<HttpRequest>()), Times.Once);
    }

    [Fact]
    public async Task Activation_SignInAfterActivation_CarriesForwardedTo()
    {
        SetupActivation(SignInOnActivation(SignupLinkMode.Embedded));
        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(new OkObjectResult(new Dictionary<string, object?> { ["token_type"] = "Bearer" }));

        var http = Http();
        var result = await Sut().CompleteActivationAsync("act-key", http.Request, http.Response);

        // Embedded has no redirect to hang the forwarded path on, so without this the value
        // configured in the portal is unreachable by any client.
        var payload = result.Should().BeOfType<ObjectResult>().Subject
            .Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload["forwardedTo"].Should().Be("/projects");
        payload["token_type"].Should().Be("Bearer");
    }

    [Fact]
    public async Task Activation_SignInAfterActivation_MfaChallenge_IsReturnedUntouched()
    {
        SetupActivation(SignInOnActivation(SignupLinkMode.Embedded));
        var challenge = new ObjectResult(new Dictionary<string, object?>
        {
            ["error"] = "mfa_enabled",
            ["mfa_required"] = true,
            ["mfa_id"] = "mfa-9"
        })
        { StatusCode = 400 };
        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(challenge);

        var http = Http();
        var result = await Sut().CompleteActivationAsync("act-key", http.Request, http.Response);

        // Not an activation response -- merging activation fields into a challenge would
        // make it look like a completed sign-in.
        result.Should().BeSameAs(challenge);
    }

    [Fact]
    public async Task Activation_EmbeddedWithoutSignIn_MintsNoLinkSession()
    {
        var link = ActivePasswordless("emb@example.com");
        link.Mode = SignupLinkMode.Embedded;
        link.ClientId = string.Empty;
        link.RedirectUri = string.Empty;
        SetupActivation(link);

        var http = Http();
        var result = await Sut().CompleteActivationAsync("act-key", http.Request, http.Response);

        // Only an OIDC authorize request can spend a link session, and an embedded link will
        // never make one. Building the authorize URL would also have thrown on the empty
        // client id and been swallowed.
        result.Should().BeNull();
        _sessions.Verify(s => s.CreateAsync(It.IsAny<LinkSessionModel>()), Times.Never);
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
    }

    [Fact]
    public async Task Redeem_AlreadyRedeemed_ReturnsInvalidLink_AndRecordsRejection()
    {
        var link = ActivePasswordless();
        link.Status = SignupLinkStatus.Redeemed;
        link.RedemptionCount = 1;
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        result.Should().BeOfType<BadRequestObjectResult>();
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.Rejected && x.RejectionReason == "exhausted")), Times.Once);
    }

    [Fact]
    public async Task Redeem_InactiveClient_ReturnsInvalidLink()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        var sut = Sut();
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://construct.example.com/callback"], false));

        var result = await sut.RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        result.Should().BeOfType<BadRequestObjectResult>();
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.RejectionReason == "client_invalid")), Times.Once);
    }
}
