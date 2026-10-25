using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth
{
    /// <summary>
    /// Pentest M6: /auth/login and /oidc/login must answer an unknown, a pending, a disabled
    /// username and a real account with a wrong password identically, attempt after attempt,
    /// including when captcha and the lock kick in. Real Mongo (user lookup with its collation,
    /// the slot reservation) and real Redis (the unknown-user counter).
    /// </summary>
    public sealed class LoginEnumerationTests : IDisposable
    {
        private const string Right = "Right#Pass1";
        private readonly RealMongo _mongo;
        private readonly RealRedis _redis;
        private readonly IMongoCollection<User> _users;
        private readonly AuthenticationRepository _repo;
        private readonly IdentityConfiguration _config = new(); // threshold 5
        private readonly Mock<ICaptchaEvaluator> _captcha = new();

        public LoginEnumerationTests()
        {
            BlocksContext.IsTestMode = true;
            _mongo = new RealMongo();
            _redis = new RealRedis();
            var tenant = _redis.NewTenant();
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenant, roles: null, userId: null, impersonated: false,
                isAuthenticated: false, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
                userName: null, phoneNumber: null, displayName: null, oauthToken: null,
                originalTenantId: tenant, impersonationSessionId: null, applicationDomain: "test"));
            _users = _mongo.Database.GetCollection<User>("Users");
            _repo = new AuthenticationRepository(_mongo.ProviderFor(), new OidcDiscoveryClient(new Mock<IHttpClientFactory>().Object), new Mock<IKeyValueStore>().Object, new XUnitTest.TestDoubles.PassThroughIamDataAccess());

            _captcha.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key", Provider = "recaptcha" });
            _captcha.Setup(c => c.VerifyAsync("good", It.IsAny<string>())).ReturnsAsync((object)new { Verified = true });

            Seed("active@x.com", u => { });
            Seed("pending@x.com", u => { u.Active = false; u.IsVerified = false; u.Status = UserLifecycleStatus.PendingVerification; });
            Seed("disabled@x.com", u => { u.Active = false; u.Status = UserLifecycleStatus.Disabled; });
            Seed("unverified@x.com", u => { u.IsVerified = false; });
        }

        public void Dispose()
        {
            _mongo.Dispose();
            _redis.Dispose();
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        public static TheoryData<string> NonAccounts => new() { "ghost@x.com", "pending@x.com", "disabled@x.com", "unverified@x.com" };

        private void Seed(string email, Action<User> change)
        {
            var user = new User
            {
                ItemId = Guid.NewGuid().ToString("n"),
                UserName = email,
                Email = email,
                Active = true,
                IsVerified = true,
                Status = UserLifecycleStatus.Active,
                Password = BCrypt.Net.BCrypt.HashPassword(Right, 4),
                OrganizationIds = ["default"]
            };
            change(user);
            _users.InsertOne(user);
        }

        private UnknownUserLoginGuard Guard() => new(_redis.CacheClient(), NullLogger<UnknownUserLoginGuard>.Instance);

        // ---------- /auth/login ----------

        private AuthenticationFlowService Embedded()
        {
            var guard = Guard();
            var verifier = new PasswordCredentialVerifier(
                NullLogger<PasswordCredentialVerifier>.Instance, new Mock<ITenants>().Object, _repo,
                new Mock<IAccountService>().Object, new Mock<IUserActivityDispatcher>().Object, new Mock<IAuthenticationDomainService>().Object);
            var password = new PasswordAuthenticationService(NullLogger<PasswordAuthenticationService>.Instance,
                new Mock<IOAuthJwtAccessTokenManager>().Object, new Mock<ICryptoService>().Object, _repo, verifier, guard);
            var strategy = new Mock<IAuthStrategy>();
            strategy.Setup(s => s.AuthenticatePasswordAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>()))
                .Returns((TokenRequest r, IdentityConfiguration c) => password.AuthenticateAsync(r, c));
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(_config);
            repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string u, string? o) => _repo.GetUserByUsernameAsync(u, o));
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);

            return new AuthenticationFlowService(repo.Object, strategy.Object, new Mock<ITokenRefresher>().Object,
                new Mock<IAuthenticationService>().Object, _captcha.Object,
                new OidcLoginAuditWriter(authDomain.Object, new Mock<IUserActivityDispatcher>().Object, NullLogger<OidcLoginAuditWriter>.Instance),
                NullLogger<AuthenticationFlowService>.Instance, new Mock<IRefreshTokenRepository>().Object,
                new Mock<IRefreshSessionResolver>().Object, guard);
        }

        /// <summary>Everything that reaches the client body and status.</summary>
        private static string Answer(AuthenticationFlowResult r) =>
            r.TokenResponse != null
                ? $"{r.TokenResponse.StatusCode}|{r.TokenResponse.Error}|{r.TokenResponse.ErrorDescription}|{r.TokenResponse.AccessToken}"
                : $"{r.StatusCode}|{r.Error}|{r.ErrorDescription}|{r.CaptchaRequired}|{r.CaptchaSiteKey}";

        private async Task<List<string>> EmbeddedRunAsync(string username, string? captchaCode, int attempts)
        {
            var sut = Embedded();
            var answers = new List<string>();
            for (var i = 0; i < attempts; i++)
            {
                answers.Add(Answer(await sut.ExecuteEmbeddedLoginAsync(
                    new EmbeddedLoginRequest { Username = username, Password = "wrong" + i, CaptchaCode = captchaCode }, new DefaultHttpContext().Request)));
            }

            return answers;
        }

        [RealRedisAndMongoTheory]
        [MemberData(nameof(NonAccounts))]
        public async Task AuthLogin_WithCaptchaAnswered_SameAnswersUntilAndAfterLock(string username)
        {
            var real = await EmbeddedRunAsync("active@x.com", "good", 7);
            var other = await EmbeddedRunAsync(username, "good", 7);

            other.Should().Equal(real);
            real[0].Should().Be($"401|{OAuthError.InValidUseNamePassword}|{OAuthError.InvalidUsernamePasswordDescription}|");
            real[4].Should().StartWith("401|", "the attempt that locks still answers 401");
            real[5].Should().StartWith("423|account_locked|");
            real[6].Should().StartWith("423|account_locked|");
        }

        [RealRedisAndMongoTheory]
        [MemberData(nameof(NonAccounts))]
        public async Task AuthLogin_WithoutCaptcha_CaptchaRequiredAtTheSameAttempt(string username)
        {
            var real = await EmbeddedRunAsync("active@x.com", null, 4);
            var other = await EmbeddedRunAsync(username, null, 4);

            other.Should().Equal(real);
            real[2].Should().Be($"400|{OAuthError.CaptchaEnabled}|Captcha verification is required|True|site-key");
        }

        [RealRedisAndMongoFact]
        public async Task AuthLogin_CaseWhitespaceAndFullWidthVariants_CountOnOneCounter_ForRealAndUnknownAlike()
        {
            var unknown = Embedded();
            foreach (var name in new[] { "Ghost@X.com", " ghost@x.com ", "ｇｈｏｓｔ@x.com" })
            {
                await unknown.ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = name, Password = "wrong", CaptchaCode = "good" }, new DefaultHttpContext().Request);
            }

            var real = Embedded();
            foreach (var name in new[] { "Active@X.com", " active@x.com ", "ａｃｔｉｖｅ@x.com" })
            {
                await real.ExecuteEmbeddedLoginAsync(new EmbeddedLoginRequest { Username = name, Password = "wrong", CaptchaCode = "good" }, new DefaultHttpContext().Request);
            }

            (await _users.Find(u => u.Email == "active@x.com").FirstAsync()).FailedLoginCount.Should().Be(3, "the lookup collation folds these to one account");
            (await Guard().GetStateAsync("ghost@x.com")).FailedCount.Should().Be(3, "the unknown counter folds them the same way");
        }

        /// <summary>
        /// The unknown-user key must fold exactly what the user lookup folds: folding more or less
        /// would let a spelling variant reach a fresh counter for one kind of name but not the other.
        /// </summary>
        [RealRedisAndMongoTheory]
        [InlineData("ACTIVE@X.COM")]
        [InlineData("\tactive@x.com ")]
        [InlineData("ａｃｔｉｖｅ@x.com")]
        [InlineData("active＠x．com")]
        [InlineData("activ\u0435@x.com")] // Cyrillic e: a different name for both
        [InlineData("activé@x.com")]       // accent: a different name for both
        [InlineData("act\u0130ve@x.com")] // dotted capital I: a different name for both
        public async Task UnknownKeyFolding_MatchesTheUserLookup(string variant)
        {
            variant = System.Text.RegularExpressions.Regex.Unescape(variant);
            var lookupFolds = await _repo.GetUserByUsernameAsync(variant) != null;
            var keyFolds = UnknownUserLoginGuard.NormalizeUsername(variant) == UnknownUserLoginGuard.NormalizeUsername("active@x.com");

            keyFolds.Should().Be(lookupFolds);
        }

        // ---------- /oidc/login ----------

        private OidcLoginOrchestrator Oidc()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync(new OidcClientRegistration { ClientId = "client-1" });
            repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(_config);
            repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string u, string? o) => _repo.GetUserByUsernameAsync(u, o));
            repo.Setup(r => r.TryReserveLoginAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Returns((string u, int t, DateTime n) => _repo.TryReserveLoginAttemptAsync(u, t, n));
            repo.Setup(r => r.ReleaseLoginAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns((string u, DateTime n) => _repo.ReleaseLoginAttemptAsync(u, n));
            repo.Setup(r => r.LockAccountIfLoginThresholdReachedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Returns((string u, int t, int d, DateTime n) => _repo.LockAccountIfLoginThresholdReachedAsync(u, t, d, n));

            var authService = new Mock<IAuthenticationService>();
            var tenants = new Mock<ITenants>();
            var cache = new Mock<ICacheClient>();
            var userRepo = new Mock<IUserRepository>();
            var endpoint = new OidcAuthorizationEndpoint(
                new OidcAuthorizationSessionStores(
                    new Mock<IAuthorizationCodeRepository>().Object,
                    new Mock<IIdpSessionRepository>().Object,
                    new Mock<Authentication.DomainService.Oidc.Services.IIdpSessionService>().Object,
                    new Mock<IPkceService>().Object,
                    new Mock<ILinkSessionRepository>().Object),
                new OidcAuthorizationIdentityStores(userRepo.Object, repo.Object, authService.Object, tenants.Object, cache.Object,
                    new Mock<IResourceRepository>().Object),
                NullLogger<OidcAuthorizationEndpoint>.Instance);
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);

            return new OidcLoginOrchestrator(repo.Object, new Mock<IMfaChallengeIssuer>().Object, authService.Object, tenants.Object,
                cache.Object, userRepo.Object, new PasswordHasher(NullLogger<PasswordHasher>.Instance),
                new OidcLoginAuditWriter(authDomain.Object, new Mock<IUserActivityDispatcher>().Object, NullLogger<OidcLoginAuditWriter>.Instance),
                new OidcCaptchaEvaluator(_captcha.Object), endpoint, NullLogger<OidcLoginOrchestrator>.Instance, Guard());
        }

        private static string Answer(IActionResult r) => r switch
        {
            ObjectResult o => $"{o.StatusCode}|{JsonSerializer.Serialize(o.Value)}",
            _ => r.GetType().Name
        };

        private async Task<List<string>> OidcRunAsync(string username, string? captchaCode, int attempts)
        {
            var sut = Oidc();
            var answers = new List<string>();
            for (var i = 0; i < attempts; i++)
            {
                answers.Add(Answer(await sut.ExecuteAsync(new OidcLoginRequest
                {
                    Username = username,
                    Password = "wrong" + i,
                    CaptchaCode = captchaCode,
                    ClientId = "client-1",
                    RedirectUri = "https://app.example.com/callback",
                    TenantId = "t1"
                }, new DefaultHttpContext().Request, new DefaultHttpContext().Response)));
            }

            return answers;
        }

        [RealRedisAndMongoTheory]
        [MemberData(nameof(NonAccounts))]
        public async Task OidcLogin_WithCaptchaAnswered_SameAnswersUntilAndAfterLock(string username)
        {
            var real = await OidcRunAsync("active@x.com", "good", 7);
            var other = await OidcRunAsync(username, "good", 7);

            other.Should().Equal(real);
            real[0].Should().Be("401|{\"error\":\"invalid_credentials\"}");
            real[4].Should().Be("423|{\"error\":\"account_locked\"}", "OIDC answers the locking attempt with 423");
            real[6].Should().Be("423|{\"error\":\"account_locked\"}");
        }

        [RealRedisAndMongoTheory]
        [MemberData(nameof(NonAccounts))]
        public async Task OidcLogin_WithoutCaptcha_CaptchaRequiredAtTheSameAttempt(string username)
        {
            var real = await OidcRunAsync("active@x.com", null, 4);
            var other = await OidcRunAsync(username, null, 4);

            other.Should().Equal(real);
            real[2].Should().Contain("captcha_enabled").And.StartWith("400|");
        }
    }
}
