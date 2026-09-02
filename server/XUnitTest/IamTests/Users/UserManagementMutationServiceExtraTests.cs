using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Users
{
    // Extra coverage for UserManagementMutationService branches NOT covered by
    // UserManagementMutationServiceTests.
    public class UserManagementMutationServiceExtraTests : IDisposable
    {
        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IValidator<UpdateMyAccountRequest>> _myAccountValidator = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();

        public UserManagementMutationServiceExtraTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>()))
                .Returns(new ValidationResult());
            _myAccountValidator.Setup(v => v.Validate(It.IsAny<UpdateMyAccountRequest>()))
                .Returns(new ValidationResult());
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserStatusChangedEvent>>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _iam.Setup(i => i.HashPassword(It.IsAny<string>(), It.IsAny<string>())).Returns((string p, string s) => "hash:" + p);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration());
            _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _userRepo.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
        }

        private static void InstallContext(string userId = "actor-1", string orgId = "default")
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object, _myAccountValidator.Object,
                _iam.Object, _userRepo.Object, _message.Object, _cache.Object, _tenants.Object, _activity.Object,
                null, _resourceRepo.Object, null);

        // ---------- MapUser provisioning-source variants ----------

        [Fact]
        public void MapUser_SocialCreationType_MapsToSocialProvisioningSource()
        {
            var user = Create().MapUser(new CreateUserRequest { Email = "s@t.com", UserCreationType = UserCreationType.Social });
            user.ProvisioningSource.Should().Be(UserProvisioningSource.Social);
        }

        [Fact]
        public void MapUser_ApiCreationType_MapsToApiProvisioningSource()
        {
            var user = Create().MapUser(new CreateUserRequest { Email = "a@t.com", UserCreationType = UserCreationType.Api });
            user.ProvisioningSource.Should().Be(UserProvisioningSource.API);
        }

        // ---------- UpdateUserAsync org resolution from context (non-default org) ----------

        [Fact]
        public async Task UpdateUser_NoCommandOrg_ResolvesContextOrgWithoutEnrollingTheUser()
        {
            // The context organization is still resolved for the guard below, but a profile edit
            // no longer grants membership as a side effect - UpdateUserAccessControlAsync owns that.
            InstallContext(orgId: "org-5");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string>() };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1", FirstName = "Ann"
            });

            result.IsSuccess.Should().BeTrue();
            user.FirstName.Should().Be("Ann");
            user.OrganizationIds.Should().BeEmpty();
            user.Roles.Should().NotContainKey("org-5");
        }

        // ---------- GetTenantConfigurationAsync (explicit interface implementation) ----------

        [Fact]
        public async Task GetTenantConfiguration_ExplicitInterface_ReturnsRepoConfig()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { ItemId = "cfg-1", IsMultiOrgEnabled = true });

            IUserManagementMutationService svc = Create();
            var cfg = await svc.GetTenantConfigurationAsync();

            cfg.Should().NotBeNull();
            cfg.ItemId.Should().Be("cfg-1");
            cfg.IsMultiOrgEnabled.Should().BeTrue();
        }

        // ---------- CreateUserByEmailActivationProcessAsync ----------

        [Fact]
        public async Task CreateUserByEmailActivationProcess_StoresKeyAndReturnsIt()
        {
            _userRepo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 45 });
            var user = new User { ItemId = "u1" };

            var (key, expiresAtUtc) = await Create().CreateUserByEmailActivationProcessAsync(user, "AccountActivation");

            key.Should().NotBeNullOrWhiteSpace();
            expiresAtUtc.Should().BeAfter(DateTime.UtcNow);
            _cache.Verify(c => c.AddStringValueAsync(key, "u1", 45 * 60), Times.Once);
            _userRepo.Verify(r => r.InsertUserKeyMapAsync(It.Is<UserKeyMap>(m => m.UserId == "u1" && m.Key == key && m.MailPurpose == "AccountActivation")), Times.Once);
        }

        // ---------- ProcessCreateUserByEmailAfterActionAsync ----------

        [Fact]
        public async Task ProcessCreateUserByEmailAfterAction_DispatchesActivityAndPostEvent()
        {
            var user = new User { ItemId = "user-77", Language = "en-US" };
            _userRepo.Setup(r => r.GetUserByIdAsync("user-77")).ReturnsAsync(user);
            _userRepo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 60 });
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<CreateUserByEmailPostEvent>())).Returns(Task.CompletedTask);

            var ok = await Create().ProcessCreateUserByEmailAfterActionAsync(
                new CreateUserByEmailEvent { Email = "e@t.com", EventType = "AccountActivation", EventQueue = "q-1", OrganizationId = "default" },
                "user-77");

            ok.Should().BeTrue();
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.UserId == "user-77")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync("q-1", It.Is<CreateUserByEmailPostEvent>(p => p.UserId == "user-77" && p.EventType == "AccountActivation" && !string.IsNullOrWhiteSpace(p.Key))), Times.Once);
        }

        // ---------- ExecuteUserMutationViaSsoCommandAsync (SendWelcomeMail = false) ----------

        [Fact]
        public async Task ExecuteUserMutationViaSso_NoWelcomeMail_SkipsEmailButAudits()
        {
            var user = new User { ItemId = "u1", Active = true };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            await Create().ExecuteUserMutationViaSsoCommandAsync(new CreateUserViaSsoEvent
            {
                ItemId = "u1", Action = MutationEventType.Update, SendWelcomeMail = false, MailPurpose = "Welcome"
            });

            _iam.Verify(i => i.SendAccountActivationEmailAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }
    }
}
