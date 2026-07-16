using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace XUnitTest.Auth
{
    public class IdpServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IAuthenticationFlowService> _flowService = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IHttpService> _httpService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICaptchaConfigurationRepository> _captchaRepo = new();
        private readonly IdpTokenExchangeClient _tokenExchange;

        private const string TenantId = "tenant-1";

        public IdpServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

            _tokenExchange = new IdpTokenExchangeClient(_httpService.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private IdpService Create() =>
            new(_authRepo.Object, _authCodeRepo.Object, _flowService.Object, _cache.Object,
                _tokenExchange, _tenants.Object, _captchaRepo.Object,
                NullLogger<IdpService>.Instance);

        private static object? Prop(object? value, string name) =>
            value?.GetType().GetProperty(name)?.GetValue(value);

        private static IdentityProvider ActiveProvider() => new()
        {
            Provider = "google",
            ProviderType = "oidc",
            IsActive = true,
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            AuthorizationUrl = "https://idp.example.com/authorize",
            TokenUrl = "https://idp.example.com/token",
            RedirectUris = new List<string> { "https://app.example.com/callback" },
            RequirePkce = false,
            Scope = "openid profile email",
            ResponseType = "code"
        };

        private static Tenant BuildTenant(List<Applications> applications) => new()
        {
            TenantId = TenantId,
            DbConnectionString = "",
            Applications = applications,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
        };

        private void SetupHttpTokenResponse(OidcTokenEndpointResponse? response, string error = "")
        {
            _httpService.Setup(h => h.SendFormUrlEncoded<OidcTokenEndpointResponse>(
                    It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((response!, error));
        }

        private void SetupFlowContext(string state, object? flowContext)
        {
            var json = flowContext == null ? null : JsonSerializer.Serialize(flowContext);
            _cache.Setup(c => c.GetStringValueAsync($"idp_flow:{state}")).ReturnsAsync(json!);
        }

        // ---------- GetUiConfigAsync ----------

        [Fact]
        public async Task GetUiConfigAsync_ReturnsNullCaptcha_WhenConfigNull()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "Captcha").Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsNullCaptcha_WhenDisabled()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = false, CaptchaKey = "k" });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "Captcha").Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsCaptcha_WhenEnabled()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key", Provider = "recaptcha", CaptchaGenerator = "gen" });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var captcha = Prop(ok.Value, "Captcha");
            captcha.Should().NotBeNull();
            Prop(captcha, "Key").Should().Be("site-key");
            Prop(captcha, "Provider").Should().Be("recaptcha");
        }

        // ---------- StartAuthenticationFlowAsync ----------

        [Fact]
        public async Task StartAuthenticationFlow_ReturnsInvalidClient_WhenProviderNull()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://app.example.com/callback", null);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task StartAuthenticationFlow_ReturnsInvalidClient_WhenProviderInactive()
        {
            var provider = ActiveProvider();
            provider.IsActive = false;
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(provider);

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://app.example.com/callback", null);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task StartAuthenticationFlow_ReturnsInvalidRequest_WhenRedirectUriBlank()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());

            var result = await Create().StartAuthenticationFlowAsync("client-1", "  ", null);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task StartAuthenticationFlow_ReturnsInvalidRedirectUri_WhenNotRegistered()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://evil.example.com/callback", null);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_redirect_uri");
        }

        [Fact]
        public async Task StartAuthenticationFlow_ReturnsAuthorizeUrl_AndCachesFlow_OnSuccess()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://app.example.com/callback", "next");

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var redirect = Prop(ok.Value, "redirect_uri") as string;
            redirect.Should().StartWith("https://idp.example.com/authorize");
            redirect.Should().Contain("client_id=client-1").And.Contain("state=");
            _cache.Verify(c => c.AddStringValueAsync(It.Is<string>(k => k.StartsWith("idp_flow:")), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        public async Task StartAuthenticationFlow_IncludesPkce_WhenRequired()
        {
            var provider = ActiveProvider();
            provider.RequirePkce = true;
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(provider);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://app.example.com/callback", null);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            (Prop(ok.Value, "redirect_uri") as string).Should().Contain("code_challenge=").And.Contain("code_challenge_method=S256");
        }

        [Fact]
        public async Task StartAuthenticationFlow_Returns500_OnException()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await Create().StartAuthenticationFlowAsync("client-1", "https://app.example.com/callback", null);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
            Prop(obj.Value, "error").Should().Be("server_error");
        }

        // ---------- HandleCallbackAsync : validation ----------

        private static (HttpRequest req, HttpResponse res) HttpPair()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");
            return (ctx.Request, ctx.Response);
        }

        [Fact]
        public async Task HandleCallback_ReturnsProviderError_WhenErrorPresent()
        {
            var (req, res) = HttpPair();

            var result = await Create().HandleCallbackAsync(null, "st", "access_denied", "user said no", req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("access_denied");
            Prop(bad.Value, "error_description").Should().Be("user said no");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidRequest_WhenCodeMissing()
        {
            var (req, res) = HttpPair();

            var result = await Create().HandleCallbackAsync(null, "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidRequest_WhenStateMissing()
        {
            var (req, res) = HttpPair();

            var result = await Create().HandleCallbackAsync("code-1", "  ", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidState_WhenFlowContextMissing()
        {
            var (req, res) = HttpPair();
            SetupFlowContext("st", null);

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_state");
        }

        [Fact]
        public async Task HandleCallback_ReturnsServerError_WhenFlowContextDeserializesNull()
        {
            var (req, res) = HttpPair();
            _cache.Setup(c => c.GetStringValueAsync("idp_flow:st")).ReturnsAsync("null");

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("server_error");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidProvider_WhenProviderMissingInContext()
        {
            var (req, res) = HttpPair();
            SetupFlowContext("st", new { tenantId = TenantId, redirectUri = "https://app.example.com/callback" });

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_provider");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidProvider_WhenProviderNotConfigured()
        {
            var (req, res) = HttpPair();
            SetupFlowContext("st", new { provider = "google", tenantId = TenantId, redirectUri = "https://app.example.com/callback" });
            _authRepo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_provider");
        }

        // ---------- HandleCallbackAsync : token exchange ----------

        private void SetupValidCallbackPrerequisites()
        {
            SetupFlowContext("st", new { provider = "google", tenantId = TenantId, redirectUri = "https://app.example.com/callback", codeVerifier = (string?)null });
            _authRepo.Setup(r => r.GetIdentityProviderAsync("google")).ReturnsAsync(ActiveProvider());
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidGrant_WhenTokenExchangeErrors()
        {
            var (req, res) = HttpPair();
            SetupValidCallbackPrerequisites();
            SetupHttpTokenResponse(null, "invalid_client");

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task HandleCallback_ReturnsInvalidGrant_WhenAccessTokenEmpty()
        {
            var (req, res) = HttpPair();
            SetupValidCallbackPrerequisites();
            SetupHttpTokenResponse(new OidcTokenEndpointResponse { AccessToken = "" });

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task HandleCallback_ReturnsImpersonated_WhenAuthCodeImpersonated()
        {
            var (req, res) = HttpPair();
            SetupValidCallbackPrerequisites();
            SetupHttpTokenResponse(new OidcTokenEndpointResponse { AccessToken = "at", RefreshToken = "rt" });
            _authCodeRepo.Setup(c => c.GetByCodeAsync("code-1")).ReturnsAsync(new AuthorizationCodeModel
            {
                Impersonated = true,
                TargetedTenantId = "target-tenant",
                ImpersonatedUserId = "imp-user",
                OrganizationId = "org-1"
            });
            _flowService.Setup(f => f.ExecuteImpersonateAsync(It.IsAny<ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(new OkObjectResult("done"));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "Impersonated").Should().Be(true);
            _flowService.Verify(f => f.ExecuteImpersonateAsync(It.IsAny<ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("idp_flow:st"), Times.Once);
        }

        [Fact]
        public async Task HandleCallback_ReturnsTokens_OnHappyPath_WhenDomainNotResolved()
        {
            var (req, res) = HttpPair();
            SetupValidCallbackPrerequisites();
            SetupHttpTokenResponse(new OidcTokenEndpointResponse { AccessToken = "at", RefreshToken = "rt", IdToken = "id", TokenType = "Bearer", ExpiresIn = 3600, Scope = "openid" });
            _authCodeRepo.Setup(c => c.GetByCodeAsync("code-1")).ReturnsAsync(new AuthorizationCodeModel { Impersonated = false });
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(BuildTenant(new List<Applications>()));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "access_token").Should().Be("at");
            Prop(ok.Value, "refresh_token").Should().Be("rt");
            Prop(ok.Value, "id_token").Should().Be("id");
            _cache.Verify(c => c.RemoveKeyAsync("idp_flow:st"), Times.Once);
        }

        [Fact]
        public async Task HandleCallback_ReturnsIdTokenOnly_WhenDomainResolved()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("app.example.com");
            ctx.Request.Headers["Origin"] = "https://app.example.com";
            var req = ctx.Request;
            var res = ctx.Response;

            SetupValidCallbackPrerequisites();
            SetupHttpTokenResponse(new OidcTokenEndpointResponse { AccessToken = "at", RefreshToken = "rt", IdToken = "id", TokenType = "Bearer", ExpiresIn = 3600, Scope = "openid" });
            _authCodeRepo.Setup(c => c.GetByCodeAsync("code-1")).ReturnsAsync(new AuthorizationCodeModel { Impersonated = false });
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(BuildTenant(new List<Applications>
            {
                new() { Domain = "app.example.com", CookieDomain = "example.com" }
            }));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "id_token").Should().Be("id");
            Prop(ok.Value, "access_token").Should().BeNull();
        }

        [Fact]
        public async Task HandleCallback_Returns500_OnUnexpectedException()
        {
            var (req, res) = HttpPair();
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("cache down"));

            var result = await Create().HandleCallbackAsync("code-1", "st", null, null, req, res);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
            Prop(obj.Value, "error").Should().Be("server_error");
        }
    }
}
