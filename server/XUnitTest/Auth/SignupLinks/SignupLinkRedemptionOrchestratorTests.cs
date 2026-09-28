using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Services;
using Authentication.DomainService.SignupLinks;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace XUnitTest.Auth.SignupLinks;

public class SignupLinkRedemptionOrchestratorTests : IDisposable
{
    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<ISignupLinkRedemptionRepository> _redemptions = new();
    private readonly Mock<ILinkSessionRepository> _sessions = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserManagementMutationService> _mutation = new();
    private readonly Mock<ICacheClient> _cache = new();
    private readonly Mock<ITenants> _tenants = new();

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

        return new SignupLinkRedemptionOrchestrator(
            _links.Object,
            _redemptions.Object,
            _sessions.Object,
            _oidc.Object,
            _users.Object,
            _mutation.Object,
            _cache.Object,
            _tenants.Object,
            config,
            NullLogger<SignupLinkRedemptionOrchestrator>.Instance);
    }

    private static SignupLink ActivePasswordless() => new()
    {
        ItemId = "link-1",
        TenantId = "t1",
        OrganizationId = "org-acme",
        CodeHash = SignupLinkCodeHasher.Hash("good-code"),
        Email = "new@example.com",
        FirstName = "Asif",
        LastName = "R",
        ClientId = "construct-web",
        RedirectUri = "https://construct.example.com/callback",
        ForwardedTo = "/projects",
        CredentialMode = SignupLinkCredentialMode.Passwordless,
        Roles = ["site-manager"],
        Permissions = [],
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

    [Fact]
    public async Task Redeem_HappyPath_CreatesUser_SetsCookie_ReturnsAuthorizeUrl()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo(
                "construct-web",
                ["https://construct.example.com/callback"],
                true,
                "Construct"));

        var mapped = new User { ItemId = "user-new", Email = link.Email };
        _mutation.Setup(m => m.MapUser(It.IsAny<CreateUserRequest>())).Returns(mapped);
        _users.Setup(u => u.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

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
        body.AuthorizeUrl.Should().Contain("client_id=construct-web");

        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _sessions.Verify(s => s.CreateAsync(It.Is<LinkSessionModel>(m =>
            m.UserId == "user-new" && m.ClientId == "construct-web" && m.OrganizationId == "org-acme")), Times.Once);
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.UserCreated && x.UserId == "user-new")), Times.Once);
        mapped.Active.Should().BeTrue();
        mapped.IsVerified.Should().BeTrue();
        mapped.UserPassType.Should().Be(UserPassType.None);
    }

    [Fact]
    public async Task Redeem_AlreadyRedeemed_ReturnsInvalidLink_AndRecordsRejection()
    {
        var link = ActivePasswordless();
        link.Status = SignupLinkStatus.Redeemed;
        link.RedemptionCount = 1;
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        var http = Http();
        var result = await Sut().RedeemAsync("good-code", "t1", http.Request, http.Response);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>()
            .Which.Error.Should().Be("invalid_link");
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.Outcome == SignupLinkRedemptionOutcome.Rejected && x.RejectionReason == "exhausted")), Times.Once);
        _users.Verify(u => u.CreateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Redeem_InactiveClient_ReturnsInvalidLink()
    {
        var link = ActivePasswordless();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync((User)null!);
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://construct.example.com/callback"], false));

        var result = await Sut().RedeemAsync("good-code", "t1", Http().Request, Http().Response);
        result.Should().BeOfType<BadRequestObjectResult>();
        _redemptions.Verify(r => r.InsertAsync(It.Is<SignupLinkRedemption>(x =>
            x.RejectionReason == "client_invalid")), Times.Once);
    }
}
