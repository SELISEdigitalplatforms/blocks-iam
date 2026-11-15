using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Nodes;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 at the OIDC code exchange: C1/C6 (stored organization re-checked, disabled → 403, no tokens),
    /// C4 (503), H4 (org_status in the body and on the claims) and the impersonated code (C12). Also the
    /// <c>org_status</c> claim written by <see cref="TokenGenerationService"/>.
    /// </summary>
    public sealed class AuthorizationCodeExchangeOrganizationGateTests : IDisposable
    {
        private const string A = "org-a";
        private const string D = "org-d";

        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo = new();
        private readonly Mock<ITokenGenerationService> _tokenService = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ITokenRevocationRepository> _tokenRevocation = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly Mock<ISecretService> _oidcSecrets = new(MockBehavior.Strict);
        private OidcClaims? _accessClaims;

        public AuthorizationCodeExchangeOrganizationGateTests()
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
                .ReturnsAsync(new ResolvedAuthorizationClaims());
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = true });
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync("id-token");
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((claims, _, _) => _accessClaims = claims)
                .ReturnsAsync("access-token");
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Consumed);
            _authCodeRepo.Setup(r => r.RecordMintAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<AuthorizationCodeMint>())).ReturnsAsync(true);
            MultiOrg(allowNoOrg: false);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthorizationCodeExchangeService Create() =>
            new(_authCodeRepo.Object, _refreshTokenRepo.Object, _tokenService.Object, _pkce.Object,
                _userRepo.Object, _claimsResolver.Object, _repo.Object, OidcClientSecretTestDoubles.Resolver(_oidcSecrets), _tenants.Object,
                _tokenRevocation.Object, _cache.Object, _resources.Object, NullLogger<AuthorizationCodeExchangeService>.Instance);

        private void MultiOrg(bool allowNoOrg) =>
            _resources.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = allowNoOrg });

        private void Arrange(string? storedOrganization, string[] memberships, string? storedStatus = null, bool impersonated = false)
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(new AuthorizationCodeModel
            {
                Code = "auth-code", ClientId = "cid", TenantId = "tenant-1", UserId = "user-1",
                OrganizationId = storedOrganization, OrganizationStatus = storedStatus, Impersonated = impersonated,
                RedirectUri = "https://app/cb", Scope = "openid profile", CodeChallenge = "", CodeChallengeMethod = "S256",
                Amr = new(), ExpiresAt = DateTime.UtcNow.AddMinutes(5), IdpSessionId = "sess-from-code"
            });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User
            {
                ItemId = "user-1", UserName = "jane", Email = "jane@x.com", OrganizationIds = [.. memberships]
            });
        }

        private static HttpRequest Request()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["code"] = "auth-code",
                ["code_verifier"] = "",
                ["client_id"] = "cid",
                ["redirect_uri"] = "https://app/cb",
                ["tenant_id"] = "",
                ["client_secret"] = ""
            });
            return ctx.Request;
        }

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        private void VerifyNoTokens()
        {
            _tokenService.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
            _tokenService.Verify(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
            _tokenService.Verify(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C6_C1_StoredOrganizationDisabledAfterAuthorize_403_NoTokens()
        {
            Arrange(A, [A, "org-b"]);
            _resources.WithOrganizationStatuses(disabled: [A]);

            var result = await Create().ExchangeAsync(Request());

            var refusal = result.Should().BeOfType<ObjectResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(refusal.Value, "error").Should().Be("organization_disabled");
            Prop(refusal.Value, "error_description").Should().Be("Your organization is disabled. Please contact your administrator or support.");
            VerifyNoTokens();
        }

        [Fact]
        public async Task C6_StoredOrganizationMissing_403()
        {
            Arrange(D, [D]);
            _resources.WithOrganizationStatuses(missing: [D]);

            var result = await Create().ExchangeAsync(Request());

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
            VerifyNoTokens();
        }

        [Fact]
        public async Task C4_StatusReadThrows_503_NoTokens()
        {
            Arrange(A, [A]);
            _resources.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());

            var result = await Create().ExchangeAsync(Request());

            var refusal = result.Should().BeOfType<ObjectResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            Prop(refusal.Value, "error").Should().Be("temporarily_unavailable");
            VerifyNoTokens();
        }

        [Fact]
        public async Task StoredOrganizationActive_MintedUnchanged_NoOrgStatus()
        {
            Arrange(A, [A]);
            _resources.WithActiveOrganizations();

            var result = await Create().ExchangeAsync(Request());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().NotBeOfType<JsonObject>();
            ok.Value!.GetType().GetProperty("org_status").Should().BeNull();
            _accessClaims!.OrgId.Should().Be(A);
            _accessClaims.OrganizationStatus.Should().BeNull();
            _claimsResolver.Verify(c => c.ResolveAsync(It.IsAny<User>(), A, It.IsAny<string>(), true), Times.Once);
        }

        [Fact]
        public async Task H4_AllDisabled_SettingTrue_OrgStatusInBodyAndClaims()
        {
            MultiOrg(allowNoOrg: true);
            Arrange(IdpConstants.NoOrganizationId, [D], storedStatus: "disabled");
            _resources.WithOrganizationStatuses(disabled: [D]);

            var result = await Create().ExchangeAsync(Request());

            var body = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<JsonObject>().Subject;
            body["org_status"]!.GetValue<string>().Should().Be("disabled");
            body["access_token"]!.GetValue<string>().Should().Be("access-token");
            _accessClaims!.OrgId.Should().Be(IdpConstants.NoOrganizationId);
            _accessClaims.OrganizationStatus.Should().Be("disabled");
            _claimsResolver.Verify(c => c.ResolveAsync(It.IsAny<User>(), IdpConstants.NoOrganizationId, It.IsAny<string>(), true), Times.Once);
        }

        [Fact]
        public async Task Q2_StoredNoOrgDisabled_ButAnOrganizationReEnabled_IssuesIt_WithoutStatus()
        {
            MultiOrg(allowNoOrg: true);
            Arrange(IdpConstants.NoOrganizationId, [A], storedStatus: "disabled");
            _resources.WithActiveOrganizations();

            var result = await Create().ExchangeAsync(Request());

            result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().NotBeOfType<JsonObject>();
            _accessClaims!.OrgId.Should().Be(A);
            _accessClaims.OrganizationStatus.Should().BeNull();
        }

        [Fact]
        public async Task C12_ImpersonatedCode_IsNotGated()
        {
            Arrange(D, [D], impersonated: true);
            _resources.WithOrganizationStatuses(disabled: [D]);

            var result = await Create().ExchangeAsync(Request());

            result.Should().BeOfType<OkObjectResult>();
            _accessClaims!.OrgId.Should().Be(D);
            _resources.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        // ---------- org_status claim on OIDC tokens ----------

        private static TokenGenerationService CreateTokenSvc()
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            return new TokenGenerationService(
                new OidcSigningKeyMaterial(),
                tenants.Object,
                new Mock<ICacheClient>().Object,
                null!,
                new Mock<ICryptoService>().Object,
                new Mock<ICertificateProviderFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IAuthenticationRepository>().Object);
        }

        [Theory]
        [InlineData("disabled", true)]
        [InlineData(null, false)]
        public async Task OidcAccessToken_OrgStatusClaim_OnlyWhenSet(string? status, bool expected)
        {
            var jwt = await CreateTokenSvc().GenerateAccessTokenAsync(new OidcClaims
            {
                Sub = "user-1", TenantId = "tenant-1", ClientId = "cid", Audience = "aud",
                OrgId = IdpConstants.NoOrganizationId, OrganizationStatus = status, Iat = 1000, AuthTime = 900
            }, "https://issuer", 3600);

            var claims = new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims;
            if (expected)
            {
                claims.Should().ContainSingle(c => c.Type == "org_status").Which.Value.Should().Be("disabled");
            }
            else
            {
                claims.Should().NotContain(c => c.Type == "org_status");
            }
        }
    }
}
