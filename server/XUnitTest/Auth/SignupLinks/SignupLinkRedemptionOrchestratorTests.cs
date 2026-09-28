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
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserManagementMutationService> _mutation = new();
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<ICacheClient> _cache = new();
    private readonly Mock<ITenants> _tenants = new();
    private readonly Mock<IMfaChallengeIssuer> _mfa = new();

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

        var stores = new SignupLinkRedemptionStores(
            _links.Object,
            _redemptions.Object,
            _sessions.Object,
            _users.Object,
            _iam.Object);
        var collaborators = new SignupLinkRedemptionCollaborators(
            _oidc.Object,
            _mutation.Object,
            _cache.Object,
            _tenants.Object,
            config,
            _mfa.Object,
            NullLogger<SignupLinkRedemptionOrchestrator>.Instance);
        return new SignupLinkRedemptionOrchestrator(stores, collaborators);
    }

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
    public async Task H2_OrganizationJoined_GrantsOrgOnly_ReturnsLoginUrl_NoSession()
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
        body.LoginUrl.Should().Contain("/oidc/login?");
        body.LoginUrl.Should().Contain("login_hint=");
        body.LoginUrl.Should().Contain("organization_id=org-acme");
        body.AuthorizeUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");

        user.OrganizationIds.Should().Contain("org-acme");
        user.Roles["org-acme"].Should().Equal("site-manager");
        user.Roles["org-other"].Should().Equal("admin");
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.OrganizationJoined)), Times.Once);
    }

    [Fact]
    public async Task C1_ExistingUserRedirected_WritesNothing()
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
        body.LoginUrl.Should().NotBeNullOrEmpty();
        body.AuthorizeUrl.Should().BeNull();
        user.Roles["org-acme"].Should().Equal(rolesBefore);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.ExistingUserRedirected)), Times.Once);
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
        await Sut().TryBindLinkSessionAfterActivationAsync("invite-key", http.Request, http.Response);
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
        await Sut().TryBindLinkSessionAfterActivationAsync("act-key", http.Request, http.Response);
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _sessions.Verify(s => s.CreateAsync(It.Is<LinkSessionModel>(m =>
            m.UserId == "user-pending" && m.Amr.Contains("link"))), Times.Once);
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
