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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        public async Task UpdateIdpSessionForLogout_CookieWinsOverFallback()
        {
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-cookie");
            _session.Setup(s => s.RevokeSessionAsync("sess-cookie", "logout_all")).ReturnsAsync(true);

            var result = await Create().UpdateIdpSessionForLogoutAsync(
                ctx,
                new System.Security.Claims.ClaimsPrincipal(),
                true,
                new[] { "sess-fallback" });

            result.Should().BeTrue();
            _session.Verify(s => s.RevokeSessionAsync("sess-cookie", "logout_all"), Times.Once);
            _session.Verify(s => s.RevokeSessionAsync("sess-fallback", "logout_all"), Times.Never);
        }

        [Fact]
        public async Task UpdateIdpSessionForLogout_NoCookie_UsesFallbackSession()
        {
            var principal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim("user_id", "actor-1")
                }));
            _session.Setup(s => s.RemoveAccountAsync("sess-fallback", "actor-1", TenantId)).ReturnsAsync(true);
            _session.Setup(s => s.GetSessionAsync("sess-fallback")).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().UpdateIdpSessionForLogoutAsync(
                new DefaultHttpContext(),
                principal,
                false,
                new[] { "sess-fallback" });

            result.Should().BeTrue();
            _session.Verify(s => s.RemoveAccountAsync("sess-fallback", "actor-1", TenantId), Times.Once);
        }

        [Fact]
        public async Task UpdateIdpSessionForLogout_EmptyPrincipal_UsesContextUserId()
        {
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-1");
            _session.Setup(s => s.RemoveAccountAsync("sess-1", "actor-1", TenantId)).ReturnsAsync(true);
            _session.Setup(s => s.GetSessionAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().UpdateIdpSessionForLogoutAsync(ctx, new System.Security.Claims.ClaimsPrincipal(), false);

            result.Should().BeTrue();
            _session.Verify(s => s.RemoveAccountAsync("sess-1", "actor-1", TenantId), Times.Once);
        }

        [Fact]
        public async Task UpdateIdpSessionForLogout_NoUserId_ReturnsFalse()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

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
            _refresh.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync((RefreshTokenModel)null!);
            var result = await Create().ProcessLogout("rt-1", new DefaultHttpContext().Request);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task LogoutUser_WhenCacheMissing_UsesPersistedRefreshTokenSessionId()
        {
            _cache.Setup(c => c.GetStringValueAsync("rt-1")).ReturnsAsync((string)null!);
            _refresh.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync(new RefreshTokenModel
            {
                TokenId = "rt-1",
                UserId = "actor-1",
                TenantId = TenantId,
                ClientId = "c1",
                SessionId = "sess-from-mongo",
                IssuedUtc = DateTime.UtcNow.AddMinutes(-5),
                SlidingExpiry = DateTime.UtcNow.AddMinutes(10),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(1)
            });
            _session.Setup(s => s.RevokeTokenAsync("rt-1", GrantTypes.RefreshToken, "c1"))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _cache.Setup(c => c.RemoveKeyAsync("rt-1")).ReturnsAsync(true);
            _refresh.Setup(r => r.RevokeByTokenIdAsync("rt-1", "logout")).ReturnsAsync(true);
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });

            var result = await Create().LogoutUser("rt-1", new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
            result.IdpSessionIds.Should().BeEquivalentTo(new[] { "sess-from-mongo" });
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

        [Fact]
        public async Task LogoutUser_ReturnsRefreshTokenSessionIdForFallback()
        {
            var cache = JsonSerializer.Serialize(new RefreshTokenCache { ClientId = "c1", RefreshToken = "rt-1", SessionId = "sess-1" });
            _cache.Setup(c => c.GetStringValueAsync("rt-1")).ReturnsAsync(cache);
            _session.Setup(s => s.RevokeTokenAsync("rt-1", GrantTypes.RefreshToken, "c1"))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _cache.Setup(c => c.RemoveKeyAsync("rt-1")).ReturnsAsync(true);
            _refresh.Setup(r => r.RevokeByTokenIdAsync("rt-1", "logout")).ReturnsAsync(true);
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });

            var result = await Create().LogoutUser("rt-1", new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
            result.IdpSessionIds.Should().BeEquivalentTo(new[] { "sess-1" });
        }

        // ---------- LogoutAll ----------

        [Fact]
        public async Task LogoutAll_RevokesActiveTokens_DispatchesTimeline()
        {
            _refresh.Setup(r => r.GetActiveTokensByUserAsync("actor-1"))
                .ReturnsAsync(new List<RefreshTokenModel> { new() { TokenId = "rt-1", SessionId = "sess-1" }, new() { TokenId = "rt-2", SessionId = "sess-2" } });
            _session.Setup(s => s.RevokeTokenAsync(It.IsAny<string>(), GrantTypes.RefreshToken, It.IsAny<string>()))
                .ReturnsAsync(new TokenRevocationResult { Success = true });
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _refresh.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "logout_all")).ReturnsAsync(2);
            _domain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _domain.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.2.3.4" });

            var result = await Create().LogoutAll(new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
            result.IdpSessionIds.Should().BeEquivalentTo(new[] { "sess-1", "sess-2" });
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

        // ---------- BuildTokenResponse / ResolveUseTokensCookie / EnsureIdpSessionForLogin ----------

        private const string AppOrigin = "https://app.example.com";

        private static string BuildJwt(string? clientId, string userId, string tenantId)
        {
            var claims = new List<Claim>
            {
                new(BlocksContext.USER_ID_CLAIM, userId),
                new(BlocksContext.TENANT_ID_CLAIM, tenantId)
            };
            if (clientId != null)
            {
                claims.Add(new Claim("client_id", clientId));
            }

            var token = new JwtSecurityToken(claims: claims);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static Tenant TenantWithApps() => new()
        {
            TenantId = TenantId,
            IsRootTenant = true,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = new List<Applications> { new() { Domain = AppOrigin, CookieDomain = ".example.com" } }
        };

        private static TokenResponse ValidTokenResponse(string accessToken) => new()
        {
            AccessToken = accessToken,
            RefreshToken = "refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = "openid email",
            IdToken = "id-token",
            ExpiresUtc = DateTime.UtcNow.AddHours(1),
            RefreshExpiresUtc = DateTime.UtcNow.AddHours(2)
        };

        [Fact]
        public async Task BuildFlowResult_TokenResponseHasError_ReturnsErrorStatus()
        {
            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult
            {
                TokenResponse = new TokenResponse { Error = "invalid_grant", ErrorDescription = "bad", StatusCode = 403 }
            }, new DefaultHttpContext());

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        }

        [Fact]
        public async Task BuildFlowResult_TokenResponseErrorWithoutStatus_DefaultsToBadRequest()
        {
            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult
            {
                TokenResponse = new TokenResponse { Error = "invalid_grant", StatusCode = 0 }
            }, new DefaultHttpContext());

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }

        [Fact]
        public async Task BuildFlowResult_ClientPrefersBody_ReturnsTokensInBodyAndCreatesSession()
        {
            var jwt = BuildJwt("client-1", "user-1", TenantId);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1", UseTokensCookie = false });
            _session.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("sess-1");

            var ctx = new DefaultHttpContext();
            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(jwt) }, ctx);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().NotBeNull();
            var payload = ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            payload.Should().ContainKey("access_token");
            _session.Verify(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BuildFlowResult_NoClientId_UsesCookies_WhenDomainResolves()
        {
            var jwt = BuildJwt(null, "user-1", TenantId);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(TenantWithApps());
            _session.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("sess-1");

            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = AppOrigin;

            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(jwt) }, ctx);

            result.Should().BeOfType<OkObjectResult>();
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        [Fact]
        public async Task BuildFlowResult_ExistingValidSession_WithMatchingAccount_UpdatesActivityAndRotates()
        {
            var jwt = BuildJwt("client-1", "user-1", TenantId);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1", UseTokensCookie = false });

            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-existing");

            _session.Setup(s => s.GetSessionAsync("sess-existing")).ReturnsAsync(new IdpSessionModel
            {
                SessionId = "sess-existing",
                Accounts = new List<IdpSessionAccount> { new() { UserId = "user-1", TenantId = TenantId } },
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(2)
            });
            _session.Setup(s => s.UpdateActivityAsync("sess-existing")).ReturnsAsync(true);
            _session.Setup(s => s.RotateSessionAsync("sess-existing", It.IsAny<string>())).ReturnsAsync("sess-rotated");

            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(jwt) }, ctx);

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.UpdateActivityAsync("sess-existing"), Times.Once);
            _session.Verify(s => s.RotateSessionAsync("sess-existing", It.IsAny<string>()), Times.Once);
            _session.Verify(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task BuildFlowResult_ExistingValidSession_WithoutMatchingAccount_AddsAccount()
        {
            var jwt = BuildJwt("client-1", "user-1", TenantId);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1", UseTokensCookie = false });

            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-existing");

            _session.Setup(s => s.GetSessionAsync("sess-existing")).ReturnsAsync(new IdpSessionModel
            {
                SessionId = "sess-existing",
                Accounts = new List<IdpSessionAccount> { new() { UserId = "other-user", TenantId = TenantId } },
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(2)
            });
            _session.Setup(s => s.AddAccountAsync("sess-existing", "user-1", TenantId, "user-1")).ReturnsAsync(true);
            _session.Setup(s => s.RotateSessionAsync("sess-existing", It.IsAny<string>())).ReturnsAsync("sess-rotated");

            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(jwt) }, ctx);

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.AddAccountAsync("sess-existing", "user-1", TenantId, "user-1"), Times.Once);
        }

        [Fact]
        public async Task BuildFlowResult_ExistingRevokedSession_CreatesNewSession()
        {
            var jwt = BuildJwt("client-1", "user-1", TenantId);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1", UseTokensCookie = false });

            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);
            var ctx = HttpContextWithCookie(cookieKey, "sess-existing");

            _session.Setup(s => s.GetSessionAsync("sess-existing")).ReturnsAsync(new IdpSessionModel
            {
                SessionId = "sess-existing",
                RevokedAt = DateTime.UtcNow,
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(2)
            });
            _session.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("sess-new");

            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(jwt) }, ctx);

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.CreateSessionAsync("user-1", TenantId, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BuildFlowResult_AccessTokenMissingClaims_SkipsSessionEnsure()
        {
            // Token with no user_id/tenant_id claims: EnsureIdpSessionForLoginAsync returns before touching the session store.
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: new[] { new Claim("client_id", "client-1") }));
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1", UseTokensCookie = false });

            var result = await Create().BuildFlowResultAsync(new AuthenticationFlowResult { TokenResponse = ValidTokenResponse(token) }, new DefaultHttpContext());

            result.Should().BeOfType<OkObjectResult>();
            _session.Verify(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- GetPrincipalFromTokenAsync ----------

        [Fact]
        public async Task GetPrincipalFromToken_TenantNotFound_ReturnsNull()
        {
            _tenants.Setup(t => t.GetTenantByID("missing")).Returns((Tenant)null!);

            var result = await Create().GetPrincipalFromTokenAsync(new DefaultHttpContext().Request, "missing");

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetPrincipalFromToken_NoToken_ReturnsNull()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(TenantWithApps());

            var result = await Create().GetPrincipalFromTokenAsync(new DefaultHttpContext().Request, TenantId);

            result.Should().BeNull();
        }
    }
}
