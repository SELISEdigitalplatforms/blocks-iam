using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Dtos;
using Authentication.DomainService.Oidc.Repositories;
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
using Microsoft.Extensions.Logging;
using Moq;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using StackExchange.Redis;

namespace XUnitTest.Auth.SignupLinks;

/// <summary>
/// #593: an already-active user confirms their password before a signup link grants anything
/// or signs them in. Names follow the spec's H#/C# acceptance criteria.
/// </summary>
public class SignupLinkExistingUserPasswordTests : IDisposable
{
    private const string Code = "good-code";
    private const string AuthPrefix = "signup_link_auth:";

    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<ISignupLinkRedemptionRepository> _redemptions = new();
    private readonly Mock<ILinkSessionRepository> _sessions = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();
    private readonly Mock<IAuthenticationRepository> _authentication = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserManagementMutationService> _mutation = new();
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<ICacheClient> _cache = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<ITenants> _tenants = new();
    private readonly Mock<IMfaChallengeIssuer> _mfa = new();
    private readonly Mock<ISignupLinkEmbeddedTokenIssuer> _embeddedTokens = new();
    private readonly Mock<IUserActivityDispatcher> _activity = new();
    private readonly Mock<IPasswordCredentialVerifier> _verifier = new();
    private readonly CapturingLogger _logger = new();

    private readonly Dictionary<string, string> _store = new();
    private readonly Dictionary<string, long> _ttl = new();
    private readonly HashSet<string> _claims = new();
    private readonly List<SignupLinkRedemption> _rows = new();
    private readonly List<LinkSessionModel> _createdSessions = new();

    public SignupLinkExistingUserPasswordTests()
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

    private SignupLinkRedemptionOrchestrator Sut(bool withVerifier = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BLOCKS_IAM_BASE_URL"] = "https://iam.example.com" })
            .Build();

        _tenants.Setup(t => t.GetTenantByID("t1")).Returns(new Tenant
        {
            TenantId = "t1",
            DbConnectionString = "",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
        });
        DomainResolver.Configure(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .Callback<string, string, long>((k, v, t) => { _store[k] = v; _ttl[k] = t; })
            .ReturnsAsync(true);
        _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string k) => _store.TryGetValue(k, out var v) ? v : null!);
        _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>()))
            .Callback<string>(k => _store.Remove(k))
            .ReturnsAsync(true);
        _cache.Setup(c => c.CacheDatabase()).Returns(_db.Object);
        _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey k, RedisValue _, TimeSpan? _, bool _, When _, CommandFlags _) => _claims.Add(k.ToString()));

        _redemptions.Setup(r => r.InsertAsync(It.IsAny<SignupLinkRedemption>()))
            .Callback<SignupLinkRedemption>(_rows.Add)
            .Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<LinkSessionModel>()))
            .Callback<LinkSessionModel>(_createdSessions.Add)
            .Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        _users.Setup(u => u.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 60 });
        _users.Setup(u => u.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://construct.example.com/callback"], true, "Construct"));
        _authentication.Setup(a => a.GetIdentityProviderByClientIdAsync("construct-web"))
            .ReturnsAsync(new Authentication.DomainService.Entities.IdentityProvider
            {
                Provider = "construct",
                ProviderType = Iam.DomainService.Utilities.IdpConstants.BlocksOidcProviderType,
                ClientId = "construct-web",
                ClientSecret = "shhh",
                TokenEndpointAuthMethod = "client_secret_post",
                IsActive = true
            });

        var stores = new SignupLinkRedemptionStores(_links.Object, _redemptions.Object, _sessions.Object, _users.Object, _iam.Object);
        var collaborators = new SignupLinkRedemptionCollaborators(
            _oidc.Object, _authentication.Object, _mutation.Object, _cache.Object, _tenants.Object, config,
            _mfa.Object, _embeddedTokens.Object, _activity.Object, _logger,
            withVerifier ? _verifier.Object : null);
        return new SignupLinkRedemptionOrchestrator(stores, collaborators);
    }

    private static SignupLink Link(string email = "jo@acme.io") => new()
    {
        ItemId = "link-1",
        TenantId = "t1",
        OrganizationId = "org-acme",
        CodeHash = SignupLinkCodeHasher.Hash(Code),
        Email = email,
        FirstName = "Jo",
        LastName = "D",
        ClientId = "construct-web",
        RedirectUri = "https://construct.example.com/callback",
        ForwardedTo = "/projects",
        CredentialMode = SignupLinkCredentialMode.Passwordless,
        Roles = ["r1"],
        Permissions = ["p1"],
        Status = SignupLinkStatus.Active,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        MaxRedemptions = 1,
        RedemptionCount = 0
    };

    private static User Active(string email = "jo@acme.io", string password = "$2a$hash", bool member = false) => new()
    {
        ItemId = "u1",
        Email = email,
        Active = true,
        IsVerified = true,
        Status = UserLifecycleStatus.Active,
        Password = password,
        OrganizationIds = member ? ["org-acme"] : ["org-other"],
        Roles = member
            ? new Dictionary<string, List<string>> { ["org-acme"] = ["old"] }
            : new Dictionary<string, List<string>> { ["org-other"] = ["admin"] },
        Permissions = new Dictionary<string, List<string>>(),
        UserMfaType = UserMfaType.Email
    };

    private static DefaultHttpContext Http()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.UserAgent = "test-agent";
        return ctx;
    }

    private void Arrange(SignupLink link, User user)
    {
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _links.Setup(l => l.GetByIdAsync(link.ItemId, link.TenantId)).ReturnsAsync(link);
        _users.Setup(u => u.GetUserByEmailAsync(link.Email)).ReturnsAsync(user);
        _users.Setup(u => u.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
    }

    private void AllowConsume(SignupLink link)
    {
        _links.Setup(l => l.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, "", It.IsAny<DateTime>()))
            .ReturnsAsync(() =>
            {
                var consumed = Link(link.Email);
                consumed.RedemptionCount = link.RedemptionCount + 1;
                consumed.Status = SignupLinkStatus.Redeemed;
                return consumed;
            });
    }

    private void Verifier(PasswordVerificationResult result) =>
        _verifier.Setup(v => v.VerifyAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<HttpRequest?>(), It.IsAny<string?>()))
            .ReturnsAsync(result);

    private static PasswordVerificationResult WrongPassword(bool lockedNow = false) => new()
    {
        Error = "invalid_username_password",
        ErrorDescription = "Invalid username or password",
        StatusCode = 401,
        AccountLockedNow = lockedNow
    };

    private async Task<RedeemSignupLinkResponse> RedeemToPasswordStepAsync(SignupLinkRedemptionOrchestrator sut)
    {
        var http = Http();
        var result = await sut.RedeemAsync(Code, "t1", http.Request, http.Response);
        return result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
    }

    private static string? Prop(IActionResult result, string name)
    {
        var value = ((ObjectResult)result).Value!;
        return value.GetType().GetProperty(name)?.GetValue(value)?.ToString();
    }

    private void SetupMfa(User user, string mfaId, bool valid = true)
    {
        var otp = new Mock<IOtpService>();
        otp.Setup(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string?>()))
            .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = mfaId });
        otp.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>()))
            .ReturnsAsync(new OtpVerificationResponse { IsValid = valid });
        _mfa.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);
    }

    // ---------------- redeem ----------------

    [Fact]
    public async Task H6_ActiveUserWithPassword_GetsAuthenticationRequired_AndNothingElseHappens()
    {
        var link = Link();
        var user = Active();
        Arrange(link, user);
        var http = Http();

        var result = await Sut().RedeemAsync(Code, "t1", http.Request, http.Response);

        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>().Subject;
        body.Error.Should().Be("authentication_required");
        body.RedemptionId.Should().HaveLength(43);
        body.MaskedEmail.Should().Be(SignupLinkCodeHasher.MaskEmail(link.Email));
        body.Mode.Should().Be("Oidc");
        body.AuthorizeUrl.Should().BeNull();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);

        var key = AuthPrefix + body.RedemptionId;
        _ttl[key].Should().Be(300);
        using var ctx = JsonDocument.Parse(_store[key]);
        ctx.RootElement.GetProperty("LinkId").GetString().Should().Be("link-1");
        ctx.RootElement.GetProperty("TenantId").GetString().Should().Be("t1");
        ctx.RootElement.GetProperty("UserId").GetString().Should().Be("u1");
        ctx.RootElement.GetProperty("Branch").GetString().Should().Be("PreExisting");
        ctx.RootElement.GetProperty("Attempts").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task H6_EmbeddedLink_ReportsEmbeddedMode()
    {
        var link = Link();
        link.Mode = SignupLinkMode.Embedded;
        link.ClientId = string.Empty;
        link.RedirectUri = string.Empty;
        Arrange(link, Active());

        var body = await RedeemToPasswordStepAsync(Sut());

        body.Error.Should().Be("authentication_required");
        body.Mode.Should().Be("Embedded");
    }

    [Fact]
    public async Task H12_AuthenticationRequired_IsRecorded_AsItsOwnOutcome_NotARejection()
    {
        var link = Link();
        link.RedemptionCount = 0;
        Arrange(link, Active());

        await RedeemToPasswordStepAsync(Sut());

        _rows.Should().ContainSingle();
        _rows[0].Outcome.Should().Be(SignupLinkRedemptionOutcome.AuthenticationRequired);
        _rows[0].RedemptionOrdinal.Should().Be(0);
        _rows[0].RejectionReason.Should().BeNull();
        _rows[0].GrantedRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task H10_SettingOff_RedeemsActiveUserExactlyAsBefore()
    {
        var link = Link();
        link.RequireExistingUserPassword = false;
        var user = Active();
        Arrange(link, user);
        AllowConsume(link);
        var http = Http();

        var result = await Sut().RedeemAsync(Code, "t1", http.Request, http.Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().StartWith("https://iam.example.com/api/oidc/authorize?");
        user.OrganizationIds.Should().Contain("org-acme");
        _rows.Should().Contain(r => r.Outcome == SignupLinkRedemptionOutcome.OrganizationJoined);
        _store.Keys.Should().NotContain(k => k.StartsWith(AuthPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task H11_PendingUser_IsActivatedByTheLink_WithoutAPasswordStep()
    {
        var link = Link();
        var user = Active();
        user.Active = false;
        user.IsVerified = false;
        user.Status = UserLifecycleStatus.PendingVerification;
        Arrange(link, user);
        AllowConsume(link);

        var http = Http();
        var result = await Sut().RedeemAsync(Code, "t1", http.Request, http.Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.Error.Should().NotBe("authentication_required");
        user.Status.Should().Be(UserLifecycleStatus.Active);
    }

    [Fact]
    public async Task C9_ActiveUserWithoutPassword_GetsPasswordNotSet_AndNothingIsGrantedOrConsumed()
    {
        var link = Link();
        var user = Active(password: "");
        Arrange(link, user);

        var result = await Sut().RedeemAsync(Code, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>();
        Prop(result, "error").Should().Be("password_not_set");
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _rows.Should().ContainSingle(r => r.Outcome == SignupLinkRedemptionOutcome.Rejected && r.RejectionReason == "password_not_set");
    }

    [Fact]
    public async Task C9_ExhaustedLink_IsRefusedBeforeThePasswordQuestion()
    {
        var link = Link();
        link.RedemptionCount = 1;
        link.Status = SignupLinkStatus.Redeemed;
        Arrange(link, Active());

        var result = await Sut().RedeemAsync(Code, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        _store.Keys.Should().NotContain(k => k.StartsWith(AuthPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task H9_LinkUserReturned_WithPassword_MustAuthenticate()
    {
        var link = Link();
        link.CreatedUserId = "u1";
        link.RedemptionCount = 1;
        link.Status = SignupLinkStatus.Redeemed;
        Arrange(link, Active(member: true));

        var body = await RedeemToPasswordStepAsync(Sut());

        body.Error.Should().Be("authentication_required");
        using var ctx = JsonDocument.Parse(_store[AuthPrefix + body.RedemptionId]);
        ctx.RootElement.GetProperty("Branch").GetString().Should().Be("LinkUserReturned");
    }

    [Fact]
    public async Task C10_LinkUserReturned_WithoutPassword_KeepsTodaysReentry()
    {
        var link = Link();
        link.CreatedUserId = "u1";
        link.RedemptionCount = 1;
        link.Status = SignupLinkStatus.Redeemed;
        Arrange(link, Active(password: "", member: true));
        var http = Http();

        var result = await Sut().RedeemAsync(Code, "t1", http.Request, http.Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        _rows.Should().ContainSingle(r => r.Outcome == SignupLinkRedemptionOutcome.LinkUserReturned);
    }

    [Fact]
    public async Task C12_AlreadyMember_SettingOff_ConcurrentExhaustion_IsRejected()
    {
        var link = Link();
        link.RequireExistingUserPassword = false;
        Arrange(link, Active(member: true));
        _links.Setup(l => l.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, "", It.IsAny<DateTime>()))
            .ReturnsAsync((SignupLink?)null);
        var http = Http();

        var result = await Sut().RedeemAsync(Code, "t1", http.Request, http.Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
        _rows.Should().ContainSingle(r => r.RejectionReason == "exhausted");
    }

    // ---------------- authenticate ----------------

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("", "pw")]
    [InlineData("rid", null)]
    [InlineData("rid", "")]
    public async Task C13_MissingFields_AreInvalidRequest(string? redemptionId, string? password)
    {
        var result = await Sut().AuthenticateAsync(redemptionId, password, null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>();
        Prop(result, "error").Should().Be("invalid_request");
    }

    [Fact]
    public async Task C4_UnknownRedemption_IsInvalidRedemption()
    {
        var result = await Sut().AuthenticateAsync("nope", "pw", null, "t1", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
        _verifier.Verify(v => v.VerifyAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<HttpRequest?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task C4_ExpiredContext_IsInvalidRedemption()
    {
        var sut = Sut();
        _store[AuthPrefix + "old"] = JsonSerializer.Serialize(new
        {
            LinkId = "link-1", TenantId = "t1", UserId = "u1", Branch = "PreExisting", Attempts = 0,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1)
        });

        var result = await sut.AuthenticateAsync("old", "pw", null, "t1", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
    }

    [Fact]
    public async Task C4_MalformedContext_IsInvalidRedemption()
    {
        var sut = Sut();
        _store[AuthPrefix + "bad"] = "{not json";

        var result = await sut.AuthenticateAsync("bad", "pw", null, "t1", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
    }

    [Theory]
    [InlineData("t2")]
    [InlineData(null)]
    public async Task C5_TenantMismatch_IsInvalidRedemption_AndReadsNoUser(string? tenant)
    {
        var link = Link();
        Arrange(link, Active());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        _users.Invocations.Clear();

        var result = await sut.AuthenticateAsync(step.RedemptionId, "pw", null, tenant, Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
        _users.Verify(u => u.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task H7_H13_CorrectPassword_GrantsOnce_ConsumesOnce_AndIssuesTheOidcSession()
    {
        var link = Link();
        var user = Active();
        Arrange(link, user);
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        var http = Http();

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", http.Request, http.Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().StartWith("https://iam.example.com/api/oidc/authorize?");
        http.Response.Headers.SetCookie.ToString().Should().Contain("blocks-link-session-t1");
        _links.Verify(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()), Times.Once);
        _users.Verify(u => u.UpdateUserAsync(user), Times.Once);
        user.Roles["org-acme"].Should().Equal("r1");
        user.Roles["org-other"].Should().Equal("admin");
        _createdSessions.Should().ContainSingle().Which.Amr.Should().Equal("link", "pwd");
        _rows.Should().Contain(r => r.Outcome == SignupLinkRedemptionOutcome.OrganizationJoined && r.RedemptionOrdinal == 1);
        _store.Should().NotContainKey(AuthPrefix + step.RedemptionId);
        _claims.Should().Contain("signup_link_auth_claim:" + step.RedemptionId);
        _verifier.Verify(v => v.VerifyAsync(user, "Correct#1", null, It.IsAny<HttpRequest?>(), "t1"), Times.Once);
    }

    [Fact]
    public async Task H7_AlreadyMember_ConsumesWithoutGranting()
    {
        var link = Link();
        var user = Active(member: true);
        Arrange(link, user);
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        user.Roles["org-acme"].Should().Equal("old");
        _rows.Should().Contain(r => r.Outcome == SignupLinkRedemptionOutcome.ExistingUserRedirected);
    }

    [Fact]
    public async Task H7_Embedded_ReturnsTokensWithForwardedTo()
    {
        var link = Link();
        link.Mode = SignupLinkMode.Embedded;
        link.ClientId = string.Empty;
        link.RedirectUri = string.Empty;
        Arrange(link, Active());
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        _embeddedTokens
            .Setup(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(), It.IsAny<List<string>>(), It.IsAny<HttpRequest>()))
            .ReturnsAsync(new OkObjectResult(new Dictionary<string, object?> { ["token_type"] = "Bearer" }));
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<ObjectResult>().Subject.Value.Should().BeOfType<Dictionary<string, object?>>()
            .Which["forwardedTo"].Should().Be("/projects");
        _embeddedTokens.Verify(t => t.IssueAsync(It.IsAny<SignupLink>(), It.IsAny<User>(),
            It.Is<List<string>>(a => a.SequenceEqual(new[] { "link", "pwd" })), It.IsAny<HttpRequest>()), Times.Once);
    }

    [Fact]
    public async Task H9_LinkUserReturned_CorrectPassword_IssuesSessionWithoutGrantOrConsumption()
    {
        var link = Link();
        link.CreatedUserId = "u1";
        link.RedemptionCount = 1;
        link.Status = SignupLinkStatus.Redeemed;
        Arrange(link, Active(member: true));
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _rows.Should().Contain(r => r.Outcome == SignupLinkRedemptionOutcome.LinkUserReturned);
    }

    [Fact]
    public async Task C1_WrongPassword_Is400_CountsTheAttempt_AndKeepsTheIdUsable()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(WrongPassword());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        var key = AuthPrefix + step.RedemptionId;

        var result = await sut.AuthenticateAsync(step.RedemptionId, "nope", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>();
        Prop(result, "error").Should().Be("invalid_username_password");
        _store.Should().ContainKey(key);
        using (var ctx = JsonDocument.Parse(_store[key]))
        {
            ctx.RootElement.GetProperty("Attempts").GetInt32().Should().Be(1);
        }
        _ttl[key].Should().BeInRange(1, 300);
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _rows.Should().Contain(r => r.RejectionReason == "invalid_password");

        // Still usable: the next (correct) attempt goes through.
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        var ok = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);
        ok.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task C6_FifthWrongPassword_DiscardsTheStep_EvenForTheCorrectPasswordAfterwards()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(WrongPassword());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        for (var i = 0; i < 5; i++)
        {
            var wrong = await sut.AuthenticateAsync(step.RedemptionId, "nope", null, "t1", Http().Request, Http().Response);
            Prop(wrong, "error").Should().Be("invalid_username_password");
        }

        _store.Should().NotContainKey(AuthPrefix + step.RedemptionId);
        _rows.Should().ContainSingle(r => r.RejectionReason == "redemption_attempts_exceeded");

        Verifier(PasswordVerificationResult.Success());
        var after = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);
        Prop(after, "error").Should().Be("invalid_redemption");
    }

    [Fact]
    public async Task C2_LockedAccount_Is423()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(new PasswordVerificationResult { Error = "account_locked", ErrorDescription = "locked", StatusCode = 423 });
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "x", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(423);
        Prop(result, "error").Should().Be("account_locked");
        _rows.Should().Contain(r => r.RejectionReason == "account_locked");
    }

    [Fact]
    public async Task C2_TheWrongAttemptThatLocks_IsAlso423()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(WrongPassword(lockedNow: true));
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "x", null, "t1", Http().Request, Http().Response);

        ((ObjectResult)result).StatusCode.Should().Be(423);
    }

    [Fact]
    public async Task C2_UserLockedAfterTheStepWasIssued_StillReachesTheVerifier()
    {
        var link = Link();
        var user = Active();
        Arrange(link, user);
        Verifier(new PasswordVerificationResult { Error = "account_locked", ErrorDescription = "locked", StatusCode = 423 });
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "x", null, "t1", Http().Request, Http().Response);

        ((ObjectResult)result).StatusCode.Should().Be(423);
    }

    [Theory]
    [InlineData("captcha_enabled", false)]
    [InlineData("captcha_invalid", true)]
    public async Task C3_Captcha_Is400WithSiteKey(string error, bool recorded)
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(new PasswordVerificationResult
        {
            Error = error, ErrorDescription = "captcha", StatusCode = 400, CaptchaRequired = true, CaptchaSiteKey = "site-key"
        });
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "x", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>();
        Prop(result, "error").Should().Be(error);
        Prop(result, "captcha_required").Should().Be("True");
        Prop(result, "captcha_site_key").Should().Be("site-key");
        _rows.Any(r => r.RejectionReason == "captcha_invalid").Should().Be(recorded);
    }

    [Fact]
    public async Task OtherVerifierFailures_PassThroughWithTheirStatus()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(new PasswordVerificationResult { Error = "auth_config_missing", StatusCode = 400 });
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "x", null, "t1", Http().Request, Http().Response);

        ((ObjectResult)result).StatusCode.Should().Be(400);
        Prop(result, "error").Should().Be("auth_config_missing");
    }

    [Fact]
    public async Task C7_ARaceOnOneId_LetsExactlyOneThrough()
    {
        var link = Link();
        Arrange(link, Active());
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        // The other request already won the SETNX claim.
        _claims.Add("signup_link_auth_claim:" + step.RedemptionId);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task C7_ClaimStoreDown_FailsClosed()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_redemption");
    }

    [Fact]
    public async Task C8_RevokedBetweenSteps_IsInvalidLink_AndGrantsNothing()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(PasswordVerificationResult.Success());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        link.Status = SignupLinkStatus.Revoked;

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        _store.Should().NotContainKey(AuthPrefix + step.RedemptionId);
        _rows.Should().Contain(r => r.RejectionReason == "revoked");
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _verifier.Verify(v => v.VerifyAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<HttpRequest?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task C8_ExhaustedBetweenSteps_IsInvalidLink()
    {
        var link = Link();
        Arrange(link, Active());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        link.RedemptionCount = 1;
        link.Status = SignupLinkStatus.Redeemed;

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        _rows.Should().Contain(r => r.RejectionReason == "exhausted");
    }

    [Fact]
    public async Task C8_LinkDeletedBetweenSteps_IsInvalidLink()
    {
        var link = Link();
        Arrange(link, Active());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        _links.Setup(l => l.GetByIdAsync("link-1", "t1")).ReturnsAsync((SignupLink?)null);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("suspended")]
    [InlineData("email")]
    public async Task UnusableOrChangedAccount_AtTheSecondStep_IsInvalidLink(string change)
    {
        var link = Link();
        var user = Active();
        Arrange(link, user);
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        switch (change)
        {
            case "deleted": _users.Setup(u => u.GetUserByIdAsync("u1")).ReturnsAsync((User)null!); break;
            case "suspended": user.Status = UserLifecycleStatus.Suspended; break;
            default: user.Email = "someone-else@acme.io"; break;
        }

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        _store.Should().NotContainKey(AuthPrefix + step.RedemptionId);
    }

    [Fact]
    public async Task C12_ExhaustedWhileFinalizing_IsInvalidLink_AndGrantsNothing()
    {
        var link = Link();
        var user = Active();
        Arrange(link, user);
        Verifier(PasswordVerificationResult.Success());
        _links.Setup(l => l.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, "", It.IsAny<DateTime>()))
            .ReturnsAsync((SignupLink?)null);
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        var http = Http();

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", http.Request, http.Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        user.OrganizationIds.Should().NotContain("org-acme");
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        http.Response.Headers.SetCookie.ToString().Should().NotContain("blocks-link-session");
    }

    [Fact]
    public async Task NoVerifierRegistered_Is500()
    {
        var link = Link();
        Arrange(link, Active());
        var sut = Sut(withVerifier: false);
        var step = await RedeemToPasswordStepAsync(sut);

        var result = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        ((ObjectResult)result).StatusCode.Should().Be(500);
    }

    // ---------------- MFA ----------------

    [Fact]
    public async Task H8_MfaUser_CorrectPassword_GetsAChallenge_AndTheGrantWaitsForTheOtp()
    {
        var link = Link();
        var user = Active();
        user.MfaEnabled = true;
        Arrange(link, user);
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        SetupMfa(user, "mfa-1");
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        var challenge = await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        challenge.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.MfaId.Should().Be("mfa-1");
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        using (var mfaCtx = JsonDocument.Parse(_store["signup_link_mfa:mfa-1"]))
        {
            mfaCtx.RootElement.GetProperty("PendingGrant").GetBoolean().Should().BeTrue();
            mfaCtx.RootElement.GetProperty("Branch").GetString().Should().Be("PreExisting");
        }

        var http = Http();
        var done = await sut.CompleteRedeemMfaAsync("mfa-1", "123456", http.Request, http.Response);

        done.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        _links.Verify(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()), Times.Once);
        user.OrganizationIds.Should().Contain("org-acme");
        _createdSessions.Should().ContainSingle().Which.Amr.Should().Equal("link", "pwd", "otp");
        _claims.Should().Contain("signup_link_mfa_claim:mfa-1");

        // A replayed OTP completion cannot grant or consume a second time.
        _store["signup_link_mfa:mfa-1"] = JsonSerializer.Serialize(new
        {
            UserId = "u1", LinkId = "link-1", TenantId = "t1", ClientId = "construct-web",
            RedirectUri = "https://construct.example.com/callback", PendingGrant = true, Branch = "PreExisting"
        });
        var replay = await sut.CompleteRedeemMfaAsync("mfa-1", "123456", Http().Request, Http().Response);
        Prop(replay, "error").Should().Be("invalid_mfa_session");
        _links.Verify(l => l.TryIncrementRedemptionAsync("link-1", "t1", "", It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task H13_TotpUser_AmrCarriesTotp()
    {
        var link = Link();
        var user = Active();
        user.MfaEnabled = true;
        user.UserMfaType = UserMfaType.TOTP;
        Arrange(link, user);
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        SetupMfa(user, "mfa-2");
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        await sut.CompleteRedeemMfaAsync("mfa-2", "123456", Http().Request, Http().Response);

        _createdSessions.Should().ContainSingle().Which.Amr.Should().Equal("link", "pwd", "totp");
    }

    [Fact]
    public async Task C11_FailedOtp_LeavesGrantsAndCountUnchanged()
    {
        var link = Link();
        var user = Active();
        user.MfaEnabled = true;
        Arrange(link, user);
        AllowConsume(link);
        Verifier(PasswordVerificationResult.Success());
        SetupMfa(user, "mfa-3", valid: false);
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);

        var result = await sut.CompleteRedeemMfaAsync("mfa-3", "000000", Http().Request, Http().Response);

        Prop(result, "error").Should().Be("invalid_mfa_code");
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        user.OrganizationIds.Should().NotContain("org-acme");
    }

    [Fact]
    public async Task C8_PendingGrantMfa_LinkRevokedMeanwhile_IsInvalidLink()
    {
        var link = Link();
        var user = Active();
        user.MfaEnabled = true;
        Arrange(link, user);
        Verifier(PasswordVerificationResult.Success());
        SetupMfa(user, "mfa-4");
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);
        await sut.AuthenticateAsync(step.RedemptionId, "Correct#1", null, "t1", Http().Request, Http().Response);
        link.Status = SignupLinkStatus.Revoked;

        var result = await sut.CompleteRedeemMfaAsync("mfa-4", "123456", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
        user.OrganizationIds.Should().NotContain("org-acme");
    }

    [Fact]
    public async Task PendingGrantMfa_LinkGone_IsInvalidLink()
    {
        var user = Active();
        user.MfaEnabled = true;
        _users.Setup(u => u.GetUserByIdAsync("u1")).ReturnsAsync(user);
        SetupMfa(user, "mfa-5");
        var sut = Sut();
        _store["signup_link_mfa:mfa-5"] = JsonSerializer.Serialize(new
        {
            UserId = "u1", LinkId = "gone", TenantId = "t1", ClientId = "", RedirectUri = "", PendingGrant = true, Branch = "PreExisting"
        });

        var result = await sut.CompleteRedeemMfaAsync("mfa-5", "123456", Http().Request, Http().Response);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<RedeemSignupLinkErrorResponse>();
    }

    [Fact]
    public async Task C17_LegacyMfaContext_WithoutPendingGrant_CompletesAsBefore()
    {
        var link = Link();
        var user = Active();
        user.MfaEnabled = true;
        Arrange(link, user);
        SetupMfa(user, "mfa-legacy");
        var sut = Sut();
        _store["signup_link_mfa:mfa-legacy"] = JsonSerializer.Serialize(new
        {
            UserId = "u1", LinkId = "link-1", TenantId = "t1", ClientId = "construct-web", RedirectUri = "https://construct.example.com/callback"
        });

        var result = await sut.CompleteRedeemMfaAsync("mfa-legacy", "123456", Http().Request, Http().Response);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RedeemSignupLinkResponse>()
            .Which.AuthorizeUrl.Should().NotBeNullOrEmpty();
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _createdSessions.Should().ContainSingle().Which.Amr.Should().Equal("link", "otp");
    }

    // ---------------- logging ----------------

    [Fact]
    public async Task C14_NeitherThePasswordNorTheRedemptionIdNorTheCodeIsLogged()
    {
        var link = Link();
        Arrange(link, Active());
        Verifier(WrongPassword());
        var sut = Sut();
        var step = await RedeemToPasswordStepAsync(sut);

        await sut.AuthenticateAsync(step.RedemptionId, "Sup3r-Secret!", null, "t1", Http().Request, Http().Response);
        await sut.AuthenticateAsync("unknown-redemption-id", "Sup3r-Secret!", null, "t1", Http().Request, Http().Response);

        _logger.Messages.Should().NotBeEmpty();
        _logger.Messages.Should().NotContain(m => m.Contains("Sup3r-Secret!", StringComparison.Ordinal));
        _logger.Messages.Should().NotContain(m => m.Contains(step.RedemptionId!, StringComparison.Ordinal));
        _logger.Messages.Should().NotContain(m => m.Contains("unknown-redemption-id", StringComparison.Ordinal));
        _logger.Messages.Should().NotContain(m => m.Contains(Code, StringComparison.Ordinal));
        _logger.Messages.Should().NotContain(m => m.Contains(link.Email, StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger<SignupLinkRedemptionCollaborators>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
