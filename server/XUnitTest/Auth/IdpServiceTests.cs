using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
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
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepo = new();
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
            _authRepo.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync((OidcUiTemplate?)null);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private IdpService Create() =>
            new(_authRepo.Object, _authCodeRepo.Object, _flowService.Object, _cache.Object,
                _tokenExchange, _tenants.Object, _captchaRepo.Object, _iamRepo.Object,
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

        // ---------- SPEC16: structured passwordPolicy ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("^(a+)+$")]
        public async Task GetUiConfig_HidesStructuredPolicy_WhenNotEnabled_EvenWithLegacyRegexStored(string? regex)
        {
            // H4: a tenant that only has a legacy regex (PasswordPolicyEnabled defaults false)
            // must never see it surfaced as passwordPolicy.
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordStrengthCheckerRegex = regex!, PasswordStrengthCheckerMessage = "legacy message"
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfig_PublishesPolicyDerivedFromTheRegex_WhenEnabled()
        {
            // H1/H2: the rule is read out of the tenant's regex as plain flags, and the pattern
            // itself never leaks into the response.
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true,
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{5,30}$",
                PasswordStrengthCheckerMessage = "At least 5 characters including one number."
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy.Should().NotBeNull();
            response.PasswordPolicy!.MinLength.Should().Be(5);
            response.PasswordPolicy.MaxLength.Should().Be(30);
            response.PasswordPolicy.RequireUppercase.Should().BeTrue();
            response.PasswordPolicy.RequireLowercase.Should().BeTrue();
            response.PasswordPolicy.RequireNumbers.Should().BeTrue();
            response.PasswordPolicy.RequireSpecialChars.Should().BeTrue();
            response.PasswordPolicy.Message.Should().Be("At least 5 characters including one number.");

            response.PasswordPolicy.RequiresServerCheck.Should().BeFalse("the flags say the whole rule");

            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            json.ToLowerInvariant().Should().NotContain("regex");
            json.Should().NotContain("(?=");
        }

        [Fact]
        public async Task GetUiConfig_PublishesOnlyTheClassesTheRegexActuallyAsserts()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true,
                PasswordStrengthCheckerRegex = @"^(?=.*[A-Z])(?=.*\d).{10,64}$"
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy!.MinLength.Should().Be(10);
            response.PasswordPolicy.MaxLength.Should().Be(64);
            response.PasswordPolicy.RequireUppercase.Should().BeTrue();
            response.PasswordPolicy.RequireNumbers.Should().BeTrue();
            response.PasswordPolicy.RequireLowercase.Should().BeFalse();
            response.PasswordPolicy.RequireSpecialChars.Should().BeFalse();
        }

        [Fact]
        public async Task GetUiConfig_ReportsNullMessage_WhenTheRegexMessageIsBlank()
        {
            // H3
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true,
                PasswordStrengthCheckerRegex = @"^.{8,30}$",
                PasswordStrengthCheckerMessage = ""
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy!.Message.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfig_AsksForAServerCheck_WhenTheFlagsCannotSayTheRule()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true,
                PasswordStrengthCheckerRegex = @"^(?=.*[a-zA-Z])(?!.*password).{8,30}$",
                PasswordStrengthCheckerMessage = "Ask IT if unsure."
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy!.RequiresServerCheck.Should().BeTrue();
            response.PasswordPolicy.MinLength.Should().Be(8);
            response.PasswordPolicy.MaxLength.Should().Be(30);
            response.PasswordPolicy.RequireUppercase.Should().BeFalse();
            response.PasswordPolicy.Message.Should().Be("Ask IT if unsure.");

            // The config endpoint is anonymous: the rule itself must never reach it.
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            json.Should().NotContain("(?=");
            json.Should().NotContain("password)");
        }

        [Fact]
        public async Task GetUiConfig_AsksForAServerCheck_ForAPatternNoBrowserCouldRun()
        {
            // Catastrophically slow: never handed to a browser, but still enforced on submit.
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true, PasswordStrengthCheckerRegex = "^(a+)+$"
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy.Should().NotBeNull();
            response.PasswordPolicy!.RequiresServerCheck.Should().BeTrue();
        }

        // ---------- POST /api/idp/password-check ----------

        private static bool MeetsRequirements(IActionResult result) =>
            (bool)Prop(((OkObjectResult)result).Value, "meetsRequirements")!;

        [Theory]
        [InlineData("Sunflower7!", true)]
        [InlineData("Sunflower!", false)]   // no digit
        [InlineData("Sun7!", false)]        // too short
        public async Task CheckPassword_AnswersForARuleTheClientCannotEvaluate(string password, bool expected)
        {
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$"
            });

            var result = await Create().CheckPasswordAsync(password);

            MeetsRequirements(result).Should().Be(expected);
        }

        [Fact]
        public async Task CheckPassword_MatchesEnforcementEvenWhereEnforcementIsCaseInsensitive()
        {
            // PasswordStrengthEvaluator matches with RegexOptions.IgnoreCase, so "(?=.*[A-Z])"
            // is satisfied by a lowercase letter and this password is accepted on submit. The
            // endpoint is the same call, so it agrees -- which is the property that matters here.
            // (That the tenant almost certainly did not intend a case-insensitive rule is a
            // separate defect in the enforcement path, not something this endpoint should paper
            // over by disagreeing with it.)
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$"
            });

            MeetsRequirements(await Create().CheckPasswordAsync("sunflower7!")).Should().BeTrue();
        }

        [Fact]
        public async Task CheckPassword_AnswersWithNothingButTheVerdict()
        {
            // The whole point of the endpoint: it says pass or fail and discloses no rule.
            var pattern = @"^(?=.*[a-zA-Z])(?!.*acmecorp).{8,30}$";
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = pattern
            });

            var result = (OkObjectResult)await Create().CheckPasswordAsync("letmein123");
            var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            json.Should().Be("{\"meetsRequirements\":true}");
            json.Should().NotContain("acmecorp");
        }

        [Fact]
        public async Task CheckPassword_HonoursTheStructuredPolicyWhenThatIsWhatIsEnforced()
        {
            // Same precedence as PasswordStrengthEvaluator, because it is the same call.
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = "^.$",
                PasswordPolicyEnabled = true,
                PasswordPolicyMinLength = 8,
                PasswordPolicyMaxLength = 30,
                PasswordPolicyRequireNumbers = true
            });

            (await Create().CheckPasswordAsync("longenough1")).Should().Match<IActionResult>(r => MeetsRequirements(r));
            (await Create().CheckPasswordAsync("longenough")).Should().Match<IActionResult>(r => !MeetsRequirements(r));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task CheckPassword_TreatsNothingAsAFailureRatherThanThrowing(string? password)
        {
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^.{8,30}$"
            });

            MeetsRequirements(await Create().CheckPasswordAsync(password)).Should().BeFalse();
        }

        [Fact]
        public async Task CheckPassword_AllowsAnythingWhenNoTenantRuleIsConfigured()
        {
            // Matches enforcement: no rule stored means nothing to fail.
            _iamRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = string.Empty
            });

            MeetsRequirements(await Create().CheckPasswordAsync("a")).Should().BeTrue();
        }

        [Theory]
        // No rule configured at all -- the only case that still publishes nothing.
        [InlineData("")]
        [InlineData(null)]
        public async Task GetUiConfig_PublishesNothing_WhenNoRuleIsConfigured(string? regex)
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                PasswordPolicyEnabled = true, PasswordStrengthCheckerRegex = regex!
            });

            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            var response = (OidcUiConfigResponse)result.Value!;

            response.PasswordPolicy.Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("zzz-not-a-tenant")]
        public async Task GetUiConfig_NoTenantConfigurationReturnsFullShape(string? tenantId)
        {
            BlocksContext.SetContext(tenantId == null ? null : BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: null, impersonated: false,
                isAuthenticated: false, requestUri: "https://test", organizationId: null,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
                userName: null, phoneNumber: null, displayName: null, oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            var result = (OkObjectResult)await Create().GetUiConfigAsync();
            result.StatusCode.Should().Be(200);
            var response = (OidcUiConfigResponse)result.Value!;
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("passwordPolicy").ValueKind.Should().Be(JsonValueKind.Null);
            document.RootElement.GetProperty("captcha").ValueKind.Should().Be(JsonValueKind.Null);
            document.RootElement.GetProperty("template").ValueKind.Should().Be(JsonValueKind.Null);
            document.RootElement.GetProperty("collectPasswordOnActivation").GetBoolean().Should().BeTrue();
        }

        // ---------- GetUiConfigAsync ----------

        [Fact]
        public async Task GetUiConfigAsync_ReturnsNullCaptchaAndTemplate_WhenNeitherIsConfigured()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);
            _authRepo.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync((OidcUiTemplate?)null);

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.Captcha.Should().BeNull();
            response.Template.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsNullCaptcha_WhenDisabled()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = false, CaptchaKey = "k" });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.Captcha.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsCaptcha_WhenEnabled()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key", Provider = "recaptcha", CaptchaGenerator = "gen" });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.Captcha.Should().BeEquivalentTo(new OidcUiCaptchaResponse
            {
                Key = "site-key",
                Provider = "recaptcha",
                Generator = "gen"
            });
            response.Template.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsCollectPasswordOnActivationFalse_WhenOidcEnabledAndFlagOff()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { IsOidcEnabled = true, CollectPasswordOnActivation = false });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.CollectPasswordOnActivation.Should().BeFalse();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsCollectPasswordOnActivationTrue_WhenOidcEnabledAndFlagOn()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { IsOidcEnabled = true, CollectPasswordOnActivation = true });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.CollectPasswordOnActivation.Should().BeTrue();
        }

        [Fact]
        public async Task GetUiConfigAsync_IgnoresStoredFlag_WhenOidcDisabled()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { IsOidcEnabled = false, CollectPasswordOnActivation = false });

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.CollectPasswordOnActivation.Should().BeTrue();
        }

        [Fact]
        public async Task GetUiConfigAsync_DefaultsCollectPasswordOnActivationTrue_WhenNoConfigurationStored()
        {
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration)null!);
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await Create().GetUiConfigAsync();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.CollectPasswordOnActivation.Should().BeTrue();
            response.PasswordPolicy.Should().BeNull();
        }

        [Fact]
        public async Task GetUiConfigAsync_ReturnsStoredTemplateWithoutModification()
        {
            var storedTemplate = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding { BrandName = "Acme Corp" },
                Theme = new OidcUiTemplateTheme { Primary = "#ff0000", Border = null },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage { Heading = "Welcome to Acme" }
                }
            };
            _authRepo.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync(storedTemplate);

            var result = await Create().GetUiConfigAsync();

            var response = ((OkObjectResult)result).Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            response.Template.Should().BeSameAs(storedTemplate);
        }

        [Fact]
        public async Task GetUiConfigAsync_PropagatesTemplateLookupFailure()
        {
            _authRepo.Setup(r => r.GetOidcUiTemplateAsync())
                .ThrowsAsync(new InvalidOperationException("store unavailable"));

            var action = () => Create().GetUiConfigAsync();

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("store unavailable");
        }

        [Fact]
        public async Task GetUiConfigAsync_PreservesCaptchaJsonContract()
        {
            var configuration = new CaptchaConfiguration
            {
                IsEnable = true,
                CaptchaKey = "site-key",
                Provider = "recaptcha",
                CaptchaGenerator = "gen"
            };
            _captchaRepo.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync(configuration);

            var result = await Create().GetUiConfigAsync();

            var response = ((OkObjectResult)result).Value.Should().BeOfType<OidcUiConfigResponse>().Subject;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var actualCaptcha = JsonSerializer.Serialize(response.Captcha, options);
            var previousCaptcha = JsonSerializer.Serialize(new
            {
                Key = configuration.CaptchaKey,
                Provider = configuration.Provider,
                Generator = configuration.CaptchaGenerator
            }, options);
            actualCaptcha.Should().Be(previousCaptcha);
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

        // ---------- StartAuthenticationFlowAsync: flow=signup ----------

        private static HttpRequest IamRequest()
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("iam.example.com");
            return context.Request;
        }

        private void SignupEnabled(bool email = true, bool sso = false)
        {
            _iamRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration
            {
                IsEmailPasswordSignUpEnabled = email,
                IsSSoSignUpEnabled = sso
            });
        }

        [Theory]
        [InlineData("signup")]
        [InlineData("SIGNUP")]
        public async Task StartAuthenticationFlow_ReturnsSignupUrl_WhenFlowIsSignup(string flow)
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            SignupEnabled();

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, flow, IamRequest());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var redirect = Prop(ok.Value, "redirect_uri") as string;
            redirect.Should().StartWith($"https://iam.example.com/oidc/signup/{TenantId}");
            Prop(ok.Value, "flow").Should().Be("signup");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupUrl_CarriesTheSpellingsTheSpaReads()
        {
            // extractOIDCParams reads redirect_uri in snake case only, and the activation-email
            // builder emits the same clientId + redirect_uri pair. Diverging here fails silently.
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            SignupEnabled();

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            var redirect = Prop((result as OkObjectResult)!.Value, "redirect_uri") as string;
            redirect.Should().Contain("clientId=client-1");
            redirect.Should().Contain($"redirect_uri={Uri.EscapeDataString("https://app.example.com/callback")}");
            redirect.Should().Contain($"tenant_id={TenantId}");
            redirect.Should().Contain("scope=").And.Contain("state=").And.Contain("nonce=");
            redirect.Should().NotContain("redirectUri=");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupCachesTheFlowContext()
        {
            // The signup page links back to /oidc/login, which replays this state. Without a
            // cached context the user would sign in successfully and then be turned away at
            // /api/idp/callback with invalid_state.
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            SignupEnabled();

            await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            _cache.Verify(c => c.AddStringValueAsync(
                It.Is<string>(k => k.StartsWith("idp_flow:")), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupCachesTheSameStateItReturns()
        {
            // The state in the URL and the state in the cache key have to be the same value,
            // or the round trip through /oidc/login cannot resolve.
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            string? cacheKey = null;
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((k, _, _) => cacheKey = k)
                .ReturnsAsync(true);
            SignupEnabled();

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            var redirect = Prop((result as OkObjectResult)!.Value, "redirect_uri") as string;
            var state = cacheKey!.Replace("idp_flow:", string.Empty);
            redirect.Should().Contain($"state={Uri.EscapeDataString(state)}");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("login")]
        public async Task StartAuthenticationFlow_KeepsLoginBehaviour_WhenFlowIsNotSignup(string? flow)
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", "next", flow, IamRequest());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            (Prop(ok.Value, "redirect_uri") as string).Should().StartWith("https://idp.example.com/authorize");
            _cache.Verify(c => c.AddStringValueAsync(It.Is<string>(k => k.StartsWith("idp_flow:")), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        [Theory]
        [InlineData("signup")]
        [InlineData(null)]
        public async Task StartAuthenticationFlow_ValidatesClientOnBothFlows(string? flow)
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);
            SignupEnabled();

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, flow, IamRequest());

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupRejectsUnregisteredRedirectUri()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            SignupEnabled();

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://evil.example.com/callback", null, "signup", IamRequest());

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_redirect_uri");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupDisabled_WhenTenantHasNoSignupMethod()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            SignupEnabled(email: false, sso: false);

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("signup_disabled");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupDisabled_WhenSsoOnlyAndNoProviderConfigured()
        {
            // The tenant flag alone is not enough: SSO-only signup with no social provider
            // renders an empty card, and the caller cannot see that from outside.
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            _authRepo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());
            SignupEnabled(email: false, sso: true);

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("signup_disabled");
        }

        [Fact]
        public async Task StartAuthenticationFlow_SignupAllowed_WhenSsoOnlyWithAnActiveSocialProvider()
        {
            var social = ActiveProvider();
            social.ProviderType = "social";
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ActiveProvider());
            _authRepo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider> { social });
            SignupEnabled(email: false, sso: true);

            var result = await Create().StartAuthenticationFlowAsync(
                "client-1", "https://app.example.com/callback", null, "signup", IamRequest());

            result.Should().BeOfType<OkObjectResult>();
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
