using Authentication.DomainService.Authentication;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;

namespace XUnitTest.Auth
{
    public class AuthenticationConfigurationServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();

        public AuthenticationConfigurationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "user-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private const string IamBaseUrl = "https://dev-iam.blocksdevelopers.com";

        private AuthenticationConfigurationService Create(string? iamBaseUrl = IamBaseUrl)
            => new(_repo.Object, _tenants.Object, BuildConfiguration(iamBaseUrl));

        private static IConfiguration BuildConfiguration(string? iamBaseUrl)
        {
            var values = new Dictionary<string, string?>();

            if (iamBaseUrl != null)
            {
                values["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = iamBaseUrl;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static Tenant TenantWithApps(params string[] domains)
        {
            return new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    PublicCertificatePath = "certs/pub.pem",
                    IssueDate = DateTime.UtcNow
                },
                Applications = domains.Select(d => new Applications { Domain = d }).ToList()
            };
        }

        public static IEnumerable<object[]> InvalidPolicies()
        {
            yield return new object[] { new string('a', 513), "", "PasswordStrengthCheckerRegex", "PasswordStrengthCheckerRegex_Too_Long" };
            yield return new object[] { "[", "", "PasswordStrengthCheckerRegex", "PasswordStrengthCheckerRegex_Invalid_Syntax" };
            yield return new object[] { "(?i)a", "", "PasswordStrengthCheckerRegex", "PasswordStrengthCheckerRegex_Not_Javascript_Compatible" };
            yield return new object[] { "^(a+)+$", "", "PasswordStrengthCheckerRegex", "PasswordStrengthCheckerRegex_Too_Slow" };
            yield return new object[] { "^.{8,64}$", new string('m', 501), "PasswordStrengthCheckerMessage", "PasswordStrengthCheckerMessage_Too_Long" };
            yield return new object[] { "", new string('m', 501), "PasswordStrengthCheckerMessage", "PasswordStrengthCheckerMessage_Too_Long" };
            yield return new object[] { "[", new string('m', 501), "PasswordStrengthCheckerRegex", "PasswordStrengthCheckerRegex_Invalid_Syntax" };
        }

        [Theory]
        [MemberData(nameof(InvalidPolicies))]
        public async Task Update_InvalidPolicyPersistsNothing(string regex, string message, string field, string code)
        {
            var current = new IdentityConfiguration { IsOidcEnabled = true, AccessTokenValidForNumberMinutes = 42 };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                PasswordStrengthCheckerRegex = regex, PasswordStrengthCheckerMessage = message,
                AccessTokenValidForNumberMinutes = 99
            });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(field, code));
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Never);
            current.AccessTokenValidForNumberMinutes.Should().Be(42);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Update_BlankPolicyKeepsLegacyValues(string? blank)
        {
            var current = new IdentityConfiguration
            {
                IsOidcEnabled = true, PasswordStrengthCheckerRegex = "^(a+)+$", PasswordStrengthCheckerMessage = "Stored message"
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                PasswordStrengthCheckerRegex = blank!, PasswordStrengthCheckerMessage = blank!
            });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.Is<IdentityConfiguration>(c =>
                c.PasswordStrengthCheckerRegex == current.PasswordStrengthCheckerRegex &&
                c.PasswordStrengthCheckerMessage == current.PasswordStrengthCheckerMessage)), Times.Once);
        }

        [Fact]
        public async Task Update_PersistsBoundaryPolicyAndAdminReadsMessage()
        {
            var regex = new string('a', 512);
            var message = new string('m', 500);
            IdentityConfiguration? stored = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => stored = c).Returns(Task.CompletedTask);
            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011", IsOidcEnabled = true,
                PasswordStrengthCheckerRegex = regex, PasswordStrengthCheckerMessage = message
            });
            result.IsSuccess.Should().BeTrue();
            stored!.PasswordStrengthCheckerRegex.Should().Be(regex);
            stored.PasswordStrengthCheckerMessage.Should().Be(message);
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(stored);
            var read = (OkObjectResult)await Create().GetAuthenticationConfigAsync();
            read.Value!.GetType().GetProperty("PasswordStrengthCheckerMessage")!.GetValue(read.Value).Should().Be(message);
        }

        // ---------- SPEC16: structured PasswordPolicy* fields ----------

        public static IEnumerable<object[]> InvalidStructuredPolicies()
        {
            // V1 -- 257 passes straight through ResolveInt as a genuine positive request value.
            // (0/negative can't reach this test via a request: ResolveInt treats <= 0 as "not
            // supplied" and falls back to current/default, both of which start at a valid
            // minimum -- see Update_RejectsMinLengthInheritedFromAnAlreadyOutOfRangeStoredValue
            // for that defensive path instead.)
            yield return new object[] { 257, 300, "", "PasswordPolicyMinLength", "PasswordPolicyMinLength_Out_Of_Range" };
            // V2
            yield return new object[] { 20, 12, "", "PasswordPolicyMaxLength", "PasswordPolicyMaxLength_Out_Of_Range" };
            yield return new object[] { 8, 257, "", "PasswordPolicyMaxLength", "PasswordPolicyMaxLength_Out_Of_Range" };
            // V3
            yield return new object[]
            {
                8, 64, new string('m', 501), "PasswordPolicyMessage", "PasswordPolicyMessage_Too_Long"
            };
        }

        [Theory]
        [MemberData(nameof(InvalidStructuredPolicies))]
        public async Task Update_InvalidStructuredPolicyPersistsNothing_WhenEnabled(
            int minLength, int maxLength, string message, string field, string code)
        {
            var current = new IdentityConfiguration { IsOidcEnabled = true, AccessTokenValidForNumberMinutes = 42 };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                PasswordPolicyMinLength = minLength,
                PasswordPolicyMaxLength = maxLength,
                PasswordPolicyMessage = message,
                AccessTokenValidForNumberMinutes = 99
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(field, code));
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Never);
            current.AccessTokenValidForNumberMinutes.Should().Be(42);
        }

        [Fact]
        public async Task Update_RejectsMinLengthInheritedFromAnAlreadyOutOfRangeStoredValue()
        {
            // Defensive: a stored document from before some future tightening could carry a
            // MinLength outside the admin-valid range. An omitted request field resolves
            // straight through to that value, and validation still catches it.
            var current = new IdentityConfiguration
            {
                IsOidcEnabled = true, PasswordPolicyMinLength = 0, PasswordPolicyMaxLength = 64
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, string>("PasswordPolicyMinLength", "PasswordPolicyMinLength_Out_Of_Range"));
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task Update_SkipsStructuredPolicyValidation_WhenNotEnabled()
        {
            // H8: an out-of-range MinLength never trips V1-V3 when the policy stays off.
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                PasswordPolicyMinLength = 0,
                PasswordPolicyMaxLength = 0
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Update_PersistsStructuredPolicy_WhenValidAndEnabled()
        {
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                PasswordPolicyMinLength = 10,
                PasswordPolicyMaxLength = 64,
                PasswordPolicyRequireUppercase = true,
                PasswordPolicyRequireNumbers = true,
                PasswordPolicyMessage = "At least 10 characters including one number."
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved.PasswordPolicyMinLength.Should().Be(10);
            saved.PasswordPolicyMaxLength.Should().Be(64);
            saved.PasswordPolicyRequireUppercase.Should().BeTrue();
            saved.PasswordPolicyRequireLowercase.Should().BeFalse();
            saved.PasswordPolicyRequireNumbers.Should().BeTrue();
            saved.PasswordPolicyRequireSpecialChars.Should().BeFalse();
            saved.PasswordPolicyMessage.Should().Be("At least 10 characters including one number.");
        }

        [Fact]
        public async Task Update_KeepsStoredStructuredPolicy_WhenRequestOmitsFields()
        {
            var current = new IdentityConfiguration
            {
                IsOidcEnabled = true,
                PasswordPolicyMinLength = 12,
                PasswordPolicyMaxLength = 40,
                PasswordPolicyRequireSpecialChars = true,
                PasswordPolicyMessage = "Stored policy message"
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011"
            });

            result.IsSuccess.Should().BeTrue();
            saved.PasswordPolicyMinLength.Should().Be(12);
            saved.PasswordPolicyMaxLength.Should().Be(40);
            saved.PasswordPolicyRequireSpecialChars.Should().BeTrue();
            saved.PasswordPolicyMessage.Should().Be("Stored policy message");
        }

        [Fact]
        public async Task Update_DoesNotForgetStoredStructuredNumbers()
        {
            // A request that says nothing about them leaves the stored numbers/message alone.
            var current = new IdentityConfiguration
            {
                IsOidcEnabled = true,
                PasswordPolicyMinLength = 12,
                PasswordPolicyMaxLength = 40,
                PasswordPolicyMessage = "Stored policy message"
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
            });

            result.IsSuccess.Should().BeTrue();
            saved.PasswordPolicyMinLength.Should().Be(12);
            saved.PasswordPolicyMaxLength.Should().Be(40);
            saved.PasswordPolicyMessage.Should().Be("Stored policy message");
        }

        [Fact]
        public async Task GetAuthenticationConfig_IncludesStructuredPolicyFields()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                PasswordPolicyMinLength = 10,
                PasswordPolicyMaxLength = 64,
                PasswordPolicyRequireUppercase = true,
                PasswordPolicyRequireLowercase = true,
                PasswordPolicyRequireNumbers = true,
                PasswordPolicyRequireSpecialChars = true,
                PasswordPolicyMessage = "Admin-facing message"
            });
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = (OkObjectResult)await Create().GetAuthenticationConfigAsync();
            var value = result.Value!;

            value.GetType().GetProperty("PasswordPolicyMinLength")!.GetValue(value).Should().Be(10);
            value.GetType().GetProperty("PasswordPolicyMaxLength")!.GetValue(value).Should().Be(64);
            value.GetType().GetProperty("PasswordPolicyRequireUppercase")!.GetValue(value).Should().Be(true);
            value.GetType().GetProperty("PasswordPolicyRequireLowercase")!.GetValue(value).Should().Be(true);
            value.GetType().GetProperty("PasswordPolicyRequireNumbers")!.GetValue(value).Should().Be(true);
            value.GetType().GetProperty("PasswordPolicyRequireSpecialChars")!.GetValue(value).Should().Be(true);
            value.GetType().GetProperty("PasswordPolicyMessage")!.GetValue(value).Should().Be("Admin-facing message");
        }

        [Fact]
        public async Task GetAuthenticationConfig_DefaultsPasswordPolicyMessageToEmpty_WhenNoConfigurationStored()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var result = (OkObjectResult)await Create().GetAuthenticationConfigAsync();
            var value = result.Value!;

            value.GetType().GetProperty("PasswordPolicyMessage")!.GetValue(value).Should().Be(string.Empty);
        }

        [Fact]
        public async Task GetAuthenticationConfig_ReturnsOk_WithCertPath()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { ItemId = ObjectId.GenerateNewId() });
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().GetAuthenticationConfigAsync();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Update_Fails_WhenNoBaseUrl_AndOidcDisabled_AndUseDefault()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = null!
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("AccountActionBaseUrl");
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task Update_Fails_WhenBaseUrl_NotInTenantAllowedDomains()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://evil.attacker.com"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("AccountActionBaseUrl");
        }

        [Fact]
        public async Task Update_Succeeds_WhenBaseUrl_InTenantAllowedDomains()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://app.example.com"
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task Update_Succeeds_WhenOidcEnabled_AndNoBaseUrl()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Update_ResolvesValuesFromCurrent_WhenRequestFieldsUnset()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccessTokenValidForNumberMinutes = 42,
                AccountActionBaseUrl = "https://app.example.com",
                IsOidcEnabled = true
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011"
                // AccessTokenValidForNumberMinutes left as 0 -> should fall back to current (42)
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.AccessTokenValidForNumberMinutes.Should().Be(42);
        }

        [Fact]
        public async Task Update_PersistsCollectPasswordOnActivation_WhenRequestSetsIt()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                CollectPasswordOnActivation = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.CollectPasswordOnActivation.Should().BeFalse();
        }

        [Fact]
        public async Task Update_KeepsStoredCollectPasswordOnActivation_WhenRequestOmitsIt()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccountActionBaseUrl = "https://app.example.com",
                IsOidcEnabled = true,
                CollectPasswordOnActivation = false
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.CollectPasswordOnActivation.Should().BeFalse();
        }

        [Fact]
        public async Task Update_DefaultsCollectPasswordOnActivationToTrue_WhenNeitherRequestNorCurrentHasIt()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
            saved!.CollectPasswordOnActivation.Should().BeTrue();
        }
        [Fact]
        public async Task Update_ForcesConfiguredIamBaseUrl_AndIgnoresPayload_WhenOidcEnabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://whatever.the-caller-asked-for.com"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be(IamBaseUrl);
        }

        [Fact]
        public async Task Update_AcceptsIamBaseUrlOutsideTenantDomains_WhenOidcEnabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            // The IAM host is deliberately not one of the tenant's application domains.
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                UseAccountActionBaseUrlAsDefault = false
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be(IamBaseUrl);
        }

        [Fact]
        public async Task Update_KeepsStoredBaseUrl_WhenOidcEnabled_AndIamBaseUrlUnresolvable()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccountActionBaseUrl = "https://already-stored.example.com",
                IsOidcEnabled = true
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create(iamBaseUrl: null).UpdateAuthenticationConfigAsync(
                new UpdateAuthenticationConfigurationRequest
                {
                    ItemId = "507f1f77bcf86cd799439011",
                    IsOidcEnabled = true,
                    AccountActionBaseUrl = "https://whatever.the-caller-asked-for.com"
                });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be("https://already-stored.example.com");
        }

        [Fact]
        public async Task Update_StillHonoursPayloadBaseUrl_WhenOidcDisabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://app.example.com"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be("https://app.example.com");
        }
    }
}
