using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Moq;
using Xunit;

namespace XUnitTest.Authentication
{
    public class MfaPolicyServiceTests
    {
        private readonly Mock<IMfaConfigurationService> _mfaConfig;
        private readonly Mock<IAuthenticationRepository> _authRepo;
        private readonly MfaPolicyService _service;

        public MfaPolicyServiceTests()
        {
            _mfaConfig = new Mock<IMfaConfigurationService>();
            _authRepo = new Mock<IAuthenticationRepository>();
            _service = new MfaPolicyService(_mfaConfig.Object, _authRepo.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<MfaPolicyService>>());
        }

        [Fact]
        public async Task Evaluate_WhenGlobalMfaDisabled_ReturnsNotRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration { EnableMfa = false });
            var user = new User { ItemId = "u1", MfaEnabled = true, UserMfaType = UserMfaType.TOTP, Roles = new Dictionary<string, List<string>>() };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.False(decision.Required);
            Assert.Equal("mfa_disabled_globally", decision.Reason);
        }

        [Fact]
        public async Task Evaluate_WhenUserEnrolledAndMethodEnabled_ReturnsRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP, UserMfaType.Email }
            });
            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = true,
                UserMfaType = UserMfaType.TOTP,
                Roles = new Dictionary<string, List<string>>()
            };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.True(decision.Required);
            Assert.Equal("user_enrolled", decision.Reason);
            Assert.Equal(UserMfaType.TOTP, decision.PreferredMethod);
        }

        [Fact]
        public async Task Evaluate_WhenRequireForAllUsers_AndUserNotEnrolled_RequiresEnrollment()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                RequireMfaForAllUsers = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });
            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                Roles = new Dictionary<string, List<string>>()
            };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.True(decision.Required);
            Assert.True(decision.MustEnrollFirst);
            Assert.Equal("global_policy", decision.Reason);
            Assert.False(decision.CanUserDisable);
        }

        [Fact]
        public async Task Evaluate_WhenRoleMatches_ReturnsRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                MfaRequiredRoles = new List<string> { "admin" },
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });
            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                Roles = new Dictionary<string, List<string>> { { "admin", new List<string>() } }
            };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.True(decision.Required);
            Assert.Equal("role_policy", decision.Reason);
        }

        [Fact]
        public async Task Evaluate_WhenRoleExempt_ReturnsNotRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                RequireMfaForAllUsers = true,
                MfaExemptRoles = new List<string> { "service-account" },
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });
            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                Roles = new Dictionary<string, List<string>> { { "service-account", new List<string>() } }
            };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.False(decision.Required);
            Assert.Equal("role_exempt", decision.Reason);
        }

        [Fact]
        public async Task Evaluate_WhenClientRequiresMfa_ReturnsRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });
            _authRepo.Setup(x => x.GetOidcClientRegistrationAsync("client-a"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "client-a", ClientId = "client-a", RequireMfa = true });

            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                Roles = new Dictionary<string, List<string>>()
            };

            var decision = await _service.EvaluateAsync(user, clientId: "client-a");

            Assert.True(decision.Required);
            Assert.Equal("client_policy", decision.Reason);
        }

        [Fact]
        public async Task Evaluate_WhenClientRestrictsMethods_IntersectsAllowed()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP, UserMfaType.Email }
            });
            _authRepo.Setup(x => x.GetOidcClientRegistrationAsync("client-b"))
                .ReturnsAsync(new OidcClientRegistration
                {
                    ItemId = "client-b",
                    ClientId = "client-b",
                    RequireMfa = true,
                    AllowedMfaMethods = new List<UserMfaType> { UserMfaType.Email }
                });

            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = true,
                UserMfaType = UserMfaType.TOTP,
                Roles = new Dictionary<string, List<string>>()
            };

            var decision = await _service.EvaluateAsync(user, clientId: "client-b");

            Assert.True(decision.Required);
            Assert.Equal(UserMfaType.Email, decision.PreferredMethod);
            Assert.DoesNotContain(UserMfaType.TOTP, decision.AllowedMethods);
            Assert.Contains(UserMfaType.Email, decision.AllowedMethods);
        }

        [Fact]
        public async Task Evaluate_WhenNoPolicyMatch_ReturnsNotRequired()
        {
            _mfaConfig.Setup(x => x.GetAsync()).ReturnsAsync(new Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });
            var user = new User
            {
                ItemId = "u1",
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                Roles = new Dictionary<string, List<string>>()
            };

            var decision = await _service.EvaluateAsync(user, clientId: null);

            Assert.False(decision.Required);
            Assert.Equal("no_policy_match", decision.Reason);
        }
    }
}
