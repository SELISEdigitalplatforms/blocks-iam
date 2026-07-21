using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    public class OidcTokenMintServiceTests : IDisposable
    {
        private readonly Mock<ITokenGenerationService> _tokenService = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<Authentication.DomainService.Oidc.Services.IIdpSessionService> _idpSession = new();

        public OidcTokenMintServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            _claimsResolver
                .Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = new() { "admin" }, Permissions = new() { "read" } });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("id-token");
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("access-token");
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            _idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("sess-9");
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OidcTokenMintService Create() =>
            new(_tokenService.Object, _claimsResolver.Object, _repo.Object, _tenants.Object,
                _idpSession.Object, NullLogger<OidcTokenMintService>.Instance);

        private static User BuildUser() => new()
        {
            ItemId = "user-1", FirstName = "Jane", LastName = "Doe", UserName = "jane", Email = "jane@x.com"
        };

        private static OidcTokenMintRequest BuildRequest(HttpRequest? request = null, List<string>? amr = null) => new()
        {
            User = BuildUser(), TenantId = "tenant-1", OrganizationId = "default",
            Scope = "openid profile", Nonce = "n-1", ClientId = "cid", Amr = amr, Request = request
        };

        [Fact]
        public async Task MintAsync_NullRequest_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => Create().MintAsync(null!));
        }

        [Fact]
        public async Task MintAsync_NullUser_Throws()
        {
            var request = new OidcTokenMintRequest { User = null!, TenantId = "tenant-1" };
            await Assert.ThrowsAsync<ArgumentException>(() => Create().MintAsync(request));
        }

        [Fact]
        public async Task MintAsync_NoHttpRequest_MintsTokens_WithoutIdpSessionOrCookies()
        {
            var result = await Create().MintAsync(BuildRequest(request: null));

            result.AccessToken.Should().Be("access-token");
            result.IdToken.Should().Be("id-token");
            result.RefreshToken.Should().Be("rt-1");
            result.EffectiveTenantId.Should().Be("tenant-1");
            result.Scope.Should().Be("openid profile");
            result.ExpiresIn.Should().BeGreaterThan(0);
            result.Domain.Should().BeNull();
            result.CanSetCookies.Should().BeFalse();

            // Request is null => no idp session resolution.
            _idpSession.Verify(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task MintAsync_BuildsClaims_FromUserAndRequest()
        {
            OidcClaims? captured = null;
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((c, _, _) => captured = c)
                .ReturnsAsync("access-token");

            await Create().MintAsync(BuildRequest(request: null));

            captured.Should().NotBeNull();
            captured!.Sub.Should().Be("user-1");
            captured.TenantId.Should().Be("tenant-1");
            captured.OrgId.Should().Be("default");
            captured.Nonce.Should().Be("n-1");
            captured.ClientId.Should().Be("cid");
            captured.Email.Should().Be("jane@x.com");
            captured.Name.Should().Be("Jane Doe");
            captured.Roles.Should().Contain("admin");
            captured.Permissions.Should().Contain("read");
        }

        [Fact]
        public async Task MintAsync_DefaultsAmrToPwd_WhenNoneProvided()
        {
            OidcClaims? captured = null;
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((c, _, _) => captured = c)
                .ReturnsAsync("id-token");

            await Create().MintAsync(BuildRequest(request: null, amr: null));

            captured!.Amr.Should().ContainSingle().Which.Should().Be("pwd");
        }

        [Fact]
        public async Task MintAsync_UsesProvidedAmr_WhenPresent()
        {
            OidcClaims? captured = null;
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((c, _, _) => captured = c)
                .ReturnsAsync("id-token");

            await Create().MintAsync(BuildRequest(request: null, amr: new() { "mfa", "otp" }));

            captured!.Amr.Should().BeEquivalentTo(new[] { "mfa", "otp" });
        }

        [Fact]
        public async Task MintAsync_WithHttpRequest_ResolvesIdpSession_AndPassesToRefreshToken()
        {
            var httpRequest = new DefaultHttpContext().Request;

            await Create().MintAsync(BuildRequest(request: httpRequest));

            _idpSession.Verify(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), "user-1", "tenant-1", It.IsAny<string?>()), Times.Once);
            _tokenService.Verify(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), false, "sess-9"), Times.Once);
        }

        [Fact]
        public async Task MintAsync_ComputesRefreshExpiry_FromConfig_WhenModelExpiryDefault()
        {
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-2", AbsoluteExpiry = default });

            var result = await Create().MintAsync(BuildRequest(request: null));

            result.RefreshToken.Should().Be("rt-2");
            result.RefreshExpiry.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task MintAsync_RequestsExplicitScopeResolution_ByDefault()
        {
            await Create().MintAsync(BuildRequest(request: null));

            _claimsResolver.Verify(c => c.ResolveAsync(It.IsAny<User>(), "default", "openid profile", true), Times.Once);
        }
    }
}
