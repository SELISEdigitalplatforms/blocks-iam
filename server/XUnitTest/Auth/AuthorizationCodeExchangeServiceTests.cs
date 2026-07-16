using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace XUnitTest.Auth
{
    public class AuthorizationCodeExchangeServiceTests : IDisposable
    {
        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo = new();
        private readonly Mock<ITokenGenerationService> _tokenService = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<Authentication.DomainService.Oidc.Services.IIdpSessionService> _idpSession = new();

        public AuthorizationCodeExchangeServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            _claimsResolver.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = new() { "admin" }, Permissions = new() { "read" } });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("id-token");
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("access-token");
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            _idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("sess-1");
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthorizationCodeExchangeService Create() =>
            new(_authCodeRepo.Object, _refreshTokenRepo.Object, _tokenService.Object, _pkce.Object,
                _userRepo.Object, _claimsResolver.Object, _repo.Object, _tenants.Object, _idpSession.Object,
                NullLogger<AuthorizationCodeExchangeService>.Instance);

        private static DefaultHttpContext BuildContext(
            string code = "auth-code", string codeVerifier = "", string clientId = "cid",
            string redirectUri = "https://app/cb", string tenantId = "", string? origin = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["tenant_id"] = tenantId
            });
            if (origin != null)
            {
                ctx.Request.Headers["Origin"] = origin;
            }
            return ctx;
        }

        private static AuthorizationCodeModel ValidAuthCode() => new()
        {
            Code = "auth-code", ClientId = "cid", TenantId = "tenant-1", UserId = "user-1",
            OrganizationId = "default", RedirectUri = "https://app/cb", Scope = "openid profile",
            CodeChallenge = "", CodeChallengeMethod = "S256", Amr = new(), ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        private static User ValidUser() => new()
        {
            ItemId = "user-1", FirstName = "Jane", LastName = "Doe", UserName = "jane", Email = "jane@x.com"
        };

        private void SetupValidExchange(OidcClientRegistration? registration = null)
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(registration ?? new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = true });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(ValidUser());
        }

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        // ---------- ValidateInputsAsync branches ----------

        [Fact]
        public async Task Exchange_MissingCode_ReturnsInvalidRequest()
        {
            var result = await Create().ExchangeAsync(BuildContext(code: "").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task Exchange_AuthCodeNotFound_ReturnsInvalidGrant()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync((AuthorizationCodeModel)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task Exchange_TenantMismatch_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.TenantId = "tenant-other";
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);

            var result = await Create().ExchangeAsync(BuildContext(tenantId: "tenant-1").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            Prop(bad.Value, "error_description").Should().Be("Tenant mismatch");
        }

        [Fact]
        public async Task Exchange_ExpiredCode_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("Authorization code has expired");
        }

        [Fact]
        public async Task Exchange_ClientNotFound_ReturnsInvalidClient()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task Exchange_ClientIdMismatch_ReturnsInvalidClient()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "different-client" });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task Exchange_RedirectUriMismatch_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.RedirectUri = "https://attacker/cb";
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });

            var result = await Create().ExchangeAsync(BuildContext(redirectUri: "https://app/cb").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("Redirect URI mismatch");
        }

        [Fact]
        public async Task Exchange_PkceInvalid_ReturnsInvalidGrant()
        {
            SetupValidExchange();
            _pkce.Setup(p => p.ValidateVerifierAsync(It.IsAny<string>(), "bad-verifier", It.IsAny<string>())).ReturnsAsync(false);

            var result = await Create().ExchangeAsync(BuildContext(codeVerifier: "bad-verifier").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("PKCE code_verifier is invalid");
        }

        [Fact]
        public async Task Exchange_UserNotFound_ReturnsInvalidGrant()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync((User)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("User not found");
        }

        [Fact]
        public async Task Exchange_UserLocked_Returns423()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            var locked = ValidUser();
            locked.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(locked);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
            Prop(obj.Value, "error").Should().Be("account_locked");
        }

        // ---------- ExchangeCoreAsync branches ----------

        [Fact]
        public async Task Exchange_HappyPath_NoResolvableDomain_ReturnsTokensInBody()
        {
            SetupValidExchange();

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "access_token").Should().Be("access-token");
            Prop(ok.Value, "id_token").Should().Be("id-token");
            Prop(ok.Value, "refresh_token").Should().Be("rt-1");
            Prop(ok.Value, "cookie_set").Should().Be(false);
        }

        [Fact]
        public async Task Exchange_AccessTokenEmpty_ReturnsServerError()
        {
            SetupValidExchange();
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(string.Empty);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("server_error");
        }

        [Fact]
        public async Task Exchange_UseTokensCookieFalse_ReturnsTokensInBody()
        {
            SetupValidExchange(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = false });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "cookie_set").Should().Be(false);
            Prop(ok.Value, "access_token").Should().Be("access-token");
        }

        [Fact]
        public async Task Exchange_HappyPath_WithResolvedDomain_SetsCookies()
        {
            SetupValidExchange();
            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = "",
                Applications = new List<Applications>
                {
                    new() { Domain = "app.example.com", CookieDomain = ".example.com", IsDomainVerified = true }
                },
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
            };
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(tenant);

            var ctx = BuildContext(origin: "https://app.example.com");
            var result = await Create().ExchangeAsync(ctx.Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "cookie_set").Should().Be(true);
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }
    }
}
