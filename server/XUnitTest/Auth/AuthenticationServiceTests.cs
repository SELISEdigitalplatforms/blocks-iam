using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace XUnitTest.Auth
{
    public class AuthenticationServiceTests : IDisposable
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IAuthenticationDomainService> _domain = new();
        private readonly Mock<IAuthSessionFacade> _session = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IRefreshTokenRepository> _refresh = new();

        private const string TenantId = "tenant-1";

        public AuthenticationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationService Create() =>
            new(NullLogger<AuthenticationService>.Instance, _cache.Object, _repo.Object, _domain.Object,
                _session.Object, _tenants.Object, _activity.Object, _refresh.Object);

        private static DefaultHttpContext HttpContextWithCookie(string? name = null, string? value = null)
        {
            var ctx = new DefaultHttpContext();
            if (name != null)
                ctx.Request.Headers["Cookie"] = $"{name}={value}";
            return ctx;
        }

        private static IdentityProvider SocialProvider(bool active = true, string type = "social") => new()
        {
            ItemId = "idp-1", Provider = "google", ProviderType = type, ClientId = "cid",
            ClientSecret = "s", TokenEndpointAuthMethod = "client_secret_post",
            DisplayName = "Google", IsActive = active, AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
            RedirectUris = new List<string> { "https://app/cb" }, Scope = "openid email"
        };

        // ---------- BuildFlowResultAsync ----------

        [Fact]
        public async Task BuildFlowResult_Error_ReturnsObjectResultWithStatus()
        {
            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult
            {
                Error = "invalid_grant", ErrorDescription = "bad", StatusCode = 401
            }, new DefaultHttpContext());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task BuildFlowResult_NoTokenResponse_ReturnsServerError()
        {
            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = null }, new DefaultHttpContext());
            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        }

        // ---------- UpdateIdpSessionForLogoutAsync ----------

        [Fact]
        public async Task UpdateIdpSessionForLogout_NoSessionCookie_ReturnsTrue()
        {
            var result = await Create().UpdateIdpSessionForLogoutAsync(new DefaultHttpContext(), new System.Security.Claims.ClaimsPrincipal(), false);
            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateIdpSessionForLogout_GlobalLogout_RevokesSession()
        {
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-1");
            _session.Setup(s => s.RevokeSessionAsync("sess-1", "logout_all")).ReturnsAsync(true);

            var result = await Create().UpdateIdpSessionForLogoutAsync(ctx, new System.Security.Claims.ClaimsPrincipal(), true);

            result.Should().BeTrue();
            _session.Verify(s => s.RevokeSessionAsync("sess-1", "logout_all"), Times.Once);
        }

        [Fact]
        public async Task UpdateIdpSessionForLogout_NoUserId_ReturnsFalse()
        {
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-1");
            var result = await Create().UpdateIdpSessionForLogoutAsync(ctx, new System.Security.Claims.ClaimsPrincipal(), false);
            result.Should().BeFalse();
        }

        // ---------- ProcessLogout ----------

        [Fact]
        public async Task ProcessLogout_NoCache_ReturnsFalse()
        {
            _cache.Setup(c => c.GetStringValueAsync("rt-1")).ReturnsAsync((string)null!);
            var result = await Create().ProcessLogout("rt-1", new DefaultHttpContext().Request);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessLogout_HappyPath_RevokesAndRemoves()
        {
            var cache = JsonSerializer.Serialize(new RefreshTokenCache { ClientId = "c1", RefreshToken = "rt-1" });
            _cache.Setup(c => c.GetStringValueAsync("rt-1")).ReturnsAsync(cache);
            _session.Setup(s => s.RevokeTokenAsync("rt-1", GrantTypes.RefreshToken, "c1"))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _cache.Setup(c => c.RemoveKeyAsync("rt-1")).ReturnsAsync(true);
            _refresh.Setup(r => r.RevokeByTokenIdAsync("rt-1", "logout")).ReturnsAsync(true);

            var result = await Create().ProcessLogout("rt-1", new DefaultHttpContext().Request);

            result.Should().BeTrue();
            _cache.Verify(c => c.RemoveKeyAsync("rt-1"), Times.Once);
            _refresh.Verify(r => r.RevokeByTokenIdAsync("rt-1", "logout"), Times.Once);
        }

        // ---------- LogoutAll ----------

        [Fact]
        public async Task LogoutAll_RevokesActiveTokens_DispatchesTimeline()
        {
            _refresh.Setup(r => r.GetActiveTokensByUserAsync("actor-1"))
                .ReturnsAsync(new List<RefreshTokenModel> { new() { TokenId = "rt-1" }, new() { TokenId = "rt-2" } });
            _session.Setup(s => s.RevokeTokenAsync(It.IsAny<string>(), GrantTypes.RefreshToken, It.IsAny<string>()))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _refresh.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "logout_all")).ReturnsAsync(2);
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });

            var result = await Create().LogoutAll(new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
            _refresh.Verify(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "logout_all"), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        // ---------- GetClientCredentialAsync ----------

        [Fact]
        public async Task GetClientCredential_PassThrough()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration { ItemId = "c1", ClientId = "c1" });
            var result = await Create().GetClientCredentialAsync("c1");
            result.ClientId.Should().Be("c1");
        }

        // ---------- GetLoginOptionsAsync ----------

        [Fact]
        public async Task GetLoginOptions_FiltersActiveSocialProviders()
        {
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>
            {
                SocialProvider(active: true),
                SocialProvider(active: false),
                SocialProvider(active: true, type: "enterprise")
            });

            var result = await Create().GetLoginOptionsAsync();
            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetLoginOptions_NoSocialProviders_SsoInfoNull()
        {
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());
            var result = await Create().GetLoginOptionsAsync();
            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- GetSocialAuthorizationUrlAsync ----------

        [Fact]
        public async Task GetSocialAuthUrl_EmptyClientId_BadRequest()
        {
            var result = await Create().GetSocialAuthorizationUrlAsync("", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetSocialAuthUrl_ProviderNotFound_NotFound()
        {
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync((IdentityProvider)null!);
            var result = await Create().GetSocialAuthorizationUrlAsync("cid", "https://app/cb");
            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetSocialAuthUrl_InactiveProvider_BadRequest()
        {
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(SocialProvider(active: false));
            var result = await Create().GetSocialAuthorizationUrlAsync("cid", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetSocialAuthUrl_NonSocialProvider_BadRequest()
        {
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(SocialProvider(type: "enterprise"));
            var result = await Create().GetSocialAuthorizationUrlAsync("cid", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetSocialAuthUrl_HappyPath_ReturnsUrl_AndCachesState()
        {
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(SocialProvider());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().GetSocialAuthorizationUrlAsync("cid", "https://app/cb");

            result.Should().BeOfType<OkObjectResult>();
            _cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        // ---------- GetOidcSocialAuthorizationUrlAsync ----------

        [Fact]
        public async Task GetOidcSocialAuthUrl_EmptyClientId_BadRequest()
        {
            var result = await Create().GetOidcSocialAuthorizationUrlAsync("", "state", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetOidcSocialAuthUrl_EmptyState_BadRequest()
        {
            var result = await Create().GetOidcSocialAuthorizationUrlAsync("cid", "", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetOidcSocialAuthUrl_InvalidOidcState_BadRequest()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);
            var result = await Create().GetOidcSocialAuthorizationUrlAsync("cid", "state", "https://app/cb");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetOidcSocialAuthUrl_HappyPath_ReturnsUrl()
        {
            _cache.Setup(c => c.GetStringValueAsync("oidc_context:state")).ReturnsAsync("{}");
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync("cid")).ReturnsAsync(SocialProvider());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().GetOidcSocialAuthorizationUrlAsync("cid", "state", "https://app/cb");

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- TriggerBackchannelLogoutAllAsync ----------

        [Fact]
        public async Task TriggerBackchannelLogoutAll_NoUris_ReturnsTrue()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ReturnsAsync(new List<OidcClientRegistration>
            {
                new() { ItemId = "c1", ClientId = "c1", BackChannelLogoutUri = null }
            });

            var result = await Create().TriggerBackchannelLogoutAllAsync(new DefaultHttpContext().Request);
            result.Should().BeTrue();
        }

        // ---------- BuildOidcUserInfoAsync ----------

        [Fact]
        public async Task BuildOidcUserInfo_NoSub_ReturnsInvalid()
        {
            var (isValid, info) = await Create().BuildOidcUserInfoAsync(new System.Security.Claims.ClaimsPrincipal());
            isValid.Should().BeFalse();
            info.Should().BeEmpty();
        }

        [Fact]
        public async Task BuildOidcUserInfo_WithSubAndProfileScope_ReturnsUserInfo()
        {
            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", "user-9"),
                new System.Security.Claims.Claim("scope", "openid profile email")
            });
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            _repo.Setup(r => r.GetUserByIdAsync("user-9")).ReturnsAsync(new User
            {
                ItemId = "user-9", FirstName = "Jane", LastName = "Doe", UserName = "jane", Email = "jane@x.com"
            });

            var (isValid, info) = await Create().BuildOidcUserInfoAsync(principal);

            isValid.Should().BeTrue();
            info["sub"].Should().Be("user-9");
            info["name"].Should().Be("Jane Doe");
            info["email"].Should().Be("jane@x.com");
        }

        // ---------- Identity provider pass-throughs ----------

        [Fact]
        public async Task IdentityProvider_PassThroughs_DelegateToDomainService()
        {
            _domain.Setup(d => d.GetIdentityProviderAsync("google")).ReturnsAsync(SocialProvider());
            _domain.Setup(d => d.GetIdentityProviderByIdAsync("idp-1")).ReturnsAsync(SocialProvider());
            _domain.Setup(d => d.GetAllIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { SocialProvider() });
            _domain.Setup(d => d.DeleteIdentityProviderAsync("idp-1")).ReturnsAsync(new BaseResponse { IsSuccess = true });
            _domain.Setup(d => d.UpdateIdentityProviderStatusAsync("idp-1", true)).ReturnsAsync(new BaseResponse { IsSuccess = true });

            var svc = Create();
            (await svc.GetIdentityProviderAsync("google")).Should().NotBeNull();
            (await svc.GetIdentityProviderByIdAsync("idp-1")).Should().NotBeNull();
            (await svc.GetAllIdentityProvidersAsync()).Should().HaveCount(1);
            (await svc.DeleteIdentityProviderAsync("idp-1")).IsSuccess.Should().BeTrue();
            (await svc.UpdateIdentityProviderStatusAsync("idp-1", true)).IsSuccess.Should().BeTrue();
        }

        // ---------- HandleTokenResponseConditionallyAsync ----------

        [Fact]
        public async Task HandleTokenResponseConditionally_Error_ReturnsErrorObject()
        {
            var response = new TokenResponse { Error = "invalid_grant", ErrorDescription = "bad", StatusCode = 400 };
            var result = await Create().HandleTokenResponseConditionallyAsync(response, new DefaultHttpContext().Response, true, "c1");
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task HandleTokenResponseConditionally_NoCookie_ReturnsTokensInBody()
        {
            var response = new TokenResponse { AccessToken = "at", RefreshToken = "rt", TokenType = "Bearer", ExpiresUtc = DateTime.UtcNow.AddMinutes(5) };
            var result = await Create().HandleTokenResponseConditionallyAsync(response, new DefaultHttpContext().Response, false, "c1");
            result.Should().NotBeNull();
        }

        // ---------- EnsureIdpSessionForOidcCallbackAsync ----------

        [Fact]
        public async Task EnsureIdpSessionForOidcCallback_EmptyUserId_ReturnsFalse()
        {
            var result = await Create().EnsureIdpSessionForOidcCallbackAsync(new DefaultHttpContext(), "", "tenant-1");
            result.Should().BeFalse();
        }
    }
}
