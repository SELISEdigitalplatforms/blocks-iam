using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using global::Authentication.DomainService.OAuth;
using global::Authentication.DomainService.Services;
using FluentAssertions;
using global::Iam.DomainService.Entities;
using global::Mfa.DomainService.Configuration;
using global::Mfa.DomainService.Services;
using Moq;

namespace XUnitTest.Auth
{
    public class MfaPolicyServiceTests
    {
        private static MfaPolicyService Create(
            out Mock<IMfaConfigurationService> configService,
            out Mock<IAuthenticationRepository> repo)
        {
            configService = new Mock<IMfaConfigurationService>();
            repo = new Mock<IAuthenticationRepository>();
            return new MfaPolicyService(configService.Object, repo.Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MfaPolicyService>.Instance);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsNotRequired_WhenUserIsNull()
        {
            var service = Create(out _, out _);

            var decision = await service.EvaluateAsync(null!, clientId: null);

            decision.Required.Should().BeFalse();
            decision.Reason.Should().Be(MfaPolicyReasons.NoUser);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsNotRequired_WhenMfaGloballyDisabled()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = false
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.Required.Should().BeFalse();
            decision.Reason.Should().Be(MfaPolicyReasons.MfaDisabledGlobally);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsNotRequired_WhenUserHasExemptRole()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email],
                MfaExemptRoles = ["admin"]
            });

            // NOTE: MfaPolicyService matches against user.Roles.Keys, so the role name must be the
            // dictionary key here (see "needs refactor" note - the service should read role values).
            var user = new User
            {
                Roles = new Dictionary<string, List<string>> { { "Admin", ["default"] } }
            };

            var decision = await service.EvaluateAsync(user, clientId: null);

            decision.Required.Should().BeFalse();
            decision.Reason.Should().Be(MfaPolicyReasons.RoleExempt);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsNoPolicyMatch_WhenNoRequirementsTrigger()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email]
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.Required.Should().BeFalse();
            decision.Reason.Should().Be(MfaPolicyReasons.NoPolicyMatch);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsGlobalPolicy_WhenRequireMfaForAllUsers_AndHasMethods()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email],
                RequireMfaForAllUsers = true
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.Required.Should().BeTrue();
            decision.Reason.Should().Be(MfaPolicyReasons.GlobalPolicy);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsRolePolicy_WhenRoleRequiresMfa()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email],
                MfaRequiredRoles = ["manager"]
            });

            // NOTE: MfaPolicyService matches against user.Roles.Keys (see "needs refactor" note).
            var user = new User
            {
                Roles = new Dictionary<string, List<string>> { { "Manager", ["default"] } }
            };

            var decision = await service.EvaluateAsync(user, clientId: null);

            decision.Required.Should().BeTrue();
            decision.Reason.Should().Be(MfaPolicyReasons.RolePolicy);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsClientPolicy_WhenClientRequiresMfa()
        {
            var service = Create(out var cfg, out var repo);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email]
            });
            repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration
                {
                    ClientId = "client-1",
                    RequireMfa = true
                });

            var decision = await service.EvaluateAsync(new User(), clientId: "client-1");

            decision.Required.Should().BeTrue();
            decision.Reason.Should().Be(MfaPolicyReasons.ClientPolicy);
        }

        [Fact]
        public async Task EvaluateAsync_ReturnsUserEnrolled_WhenUserHasMfaEnabled()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email]
            });

            var user = new User
            {
                MfaEnabled = true,
                UserMfaType = UserMfaType.Email
            };

            var decision = await service.EvaluateAsync(user, clientId: null);

            decision.Required.Should().BeTrue();
            decision.Reason.Should().Be(MfaPolicyReasons.UserEnrolled);
        }

        [Fact]
        public async Task EvaluateAsync_SetsMustEnrollFirst_WhenRequiredButNotEnrolled()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email],
                RequireMfaForAllUsers = true
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.Required.Should().BeTrue();
            decision.MustEnrollFirst.Should().BeTrue();
        }

        [Fact]
        public async Task EvaluateAsync_UserCannotDisable_WhenRequireMfaForAllUsers()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email],
                RequireMfaForAllUsers = true,
                AllowUserOptOut = true
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.CanUserDisable.Should().BeFalse();
        }

        [Fact]
        public async Task EvaluateAsync_DefaultsAllowedMethods_ToUserMfaConfig_WhenNull()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true
            });

            var decision = await service.EvaluateAsync(new User(), clientId: null);

            decision.AllowedMethods.Should().NotBeNull();
            decision.AllowedMethods.Should().BeEmpty();
        }

        [Fact]
        public async Task EvaluateAsync_PrefersUserMfaType_WhenApplicable()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email, UserMfaType.TOTP]
            });

            var user = new User
            {
                MfaEnabled = true,
                UserMfaType = UserMfaType.TOTP
            };

            var decision = await service.EvaluateAsync(user, clientId: null);

            decision.PreferredMethod.Should().Be(UserMfaType.TOTP);
        }

        [Fact]
        public async Task EvaluateAsync_FallsBackToFirstAllowedMethod_WhenUserTypeNotApplicable()
        {
            var service = Create(out var cfg, out _);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email, UserMfaType.TOTP],
                RequireMfaForAllUsers = true
            });

            var user = new User
            {
                MfaEnabled = true,
                UserMfaType = UserMfaType.None
            };

            var decision = await service.EvaluateAsync(user, clientId: null);

            decision.PreferredMethod.Should().Be(UserMfaType.Email);
        }

        [Fact]
        public async Task EvaluateAsync_RestrictsMethodsToClientAllowed()
        {
            var service = Create(out var cfg, out var repo);
            cfg.Setup(c => c.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = [UserMfaType.Email, UserMfaType.TOTP]
            });
            repo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration
                {
                    ClientId = "client-1",
                    RequireMfa = true,
                    AllowedMfaMethods = [UserMfaType.Email]
                });

            var decision = await service.EvaluateAsync(new User(), clientId: "client-1");

            decision.Required.Should().BeTrue();
            decision.AllowedMethods.Should().BeEquivalentTo([UserMfaType.Email]);
        }
    }
}