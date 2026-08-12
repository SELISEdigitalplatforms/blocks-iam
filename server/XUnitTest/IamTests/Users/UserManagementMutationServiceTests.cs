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
    public class UserManagementMutationServiceTests : IDisposable
    {
        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();

        public UserManagementMutationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>()))
                .Returns(new ValidationResult());
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserStatusChangedEvent>>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateUserViaSsoEvent>>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _iam.Setup(i => i.HashPassword(It.IsAny<string>(), It.IsAny<string>())).Returns((string p, string s) => "hash:" + p);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration());
            _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
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
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object,
                _iam.Object, _userRepo.Object, _message.Object, _cache.Object, _tenants.Object, _activity.Object,
                null, _resourceRepo.Object, null);

        private static ValidationResult Invalid(string prop = "Email", string msg = "required") =>
            new(new[] { new ValidationFailure(prop, msg) });

        // ---------- CreateUserAsync ----------

        [Fact]
        public async Task CreateUser_ValidationFails_ReturnsErrors()
        {
            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid());

            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = "" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
            _userRepo.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task CreateUser_HappyPath_CreatesUser_SendsEvent()
        {
            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = " New@Test.com ", Password = "pw" });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrWhiteSpace();
            _userRepo.Verify(r => r.CreateUserAsync(It.Is<User>(u => u.Email == "new@test.com")), Times.Once);
            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()), Times.Once);
        }

        [Fact]
        public async Task CreateUser_TooManyPermissions_ReturnsValidationError()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var command = new CreateUserRequest
            {
                Email = "x@test.com",
                OrganizationId = "org-9",
                Permissions = new List<string> { "p1", "p2", "p3", "p4", "p5", "p6" }
            };
            _resourceRepo.Setup(r => r.GetOrganizationById("org-9")).ReturnsAsync((Organization)null!);

            var result = await Create().CreateUserAsync(command);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(nameof(command.Permissions));
        }

        [Fact]
        public async Task ProcessCreateUser_MultiOrg_UsesOrgDefaultsForRolesAndPermissions()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _resourceRepo.Setup(r => r.GetOrganizationById("org-5")).ReturnsAsync(new Organization
            {
                Name = "Org5",
                DefaultRoleForMembers = new List<string> { "member" },
                DefaultPermissionsForMembers = new List<string> { "read" }
            });

            var command = new CreateUserRequest { Email = "y@test.com", OrganizationId = "org-5", Roles = new(), Permissions = null! };
            var id = await Create().ProcessCreateUserAsync(command);

            id.Should().NotBeNullOrWhiteSpace();
            command.Roles.Should().Contain("member");
            command.Permissions.Should().Contain("read");
        }

        [Fact]
        public void MapUser_NoPassword_LeavesPasswordEmpty_AndPendingVerification()
        {
            var user = Create().MapUser(new CreateUserRequest { Email = " Z@Test.com ", Password = null, VerifiedType = UserVerifiedType.None });

            user.Password.Should().BeEmpty();
            user.Status.Should().Be(UserLifecycleStatus.PendingVerification);
            user.Email.Should().Be("z@test.com");
            user.UserName.Should().Be("z@test.com");
        }

        [Fact]
        public void MapUser_WithPassword_HashesAndActive()
        {
            var user = Create().MapUser(new CreateUserRequest { Email = "z@test.com", Password = "secret", VerifiedType = UserVerifiedType.Email });

            user.Password.Should().Be("hash:secret");
            user.Status.Should().Be(UserLifecycleStatus.Active);
            user.EmailVerifiedAtUtc.Should().NotBeNull();
        }

        // ---------- UpdateUserAsync ----------

        [Fact]
        public async Task UpdateUser_ValidationFails_ReturnsErrors()
        {
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>())).Returns(Invalid("ItemId"));

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
        }

        [Fact]
        public async Task UpdateUser_NotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1" });

            result.Errors.Should().ContainKey("ItemId");
        }

        [Fact]
        public async Task UpdateUser_RepositoryFails_ReturnsUnsuccessful()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1", OrganizationIds = new List<string> { "default" } });
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default" });

            result.IsSuccess.Should().BeFalse();
        }

        // ---------- DeactivateUserAsync ----------

        [Fact]
        public async Task Deactivate_NotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("missing")).ReturnsAsync((User)null!);

            var result = await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "missing" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("user_not_found");
        }

        [Fact]
        public async Task Deactivate_HappyPath_DisablesUser()
        {
            var user = new User { ItemId = "u1", Active = true };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "u1" });

            result.IsSuccess.Should().BeTrue();
            user.Active.Should().BeFalse();
            user.Status.Should().Be(UserLifecycleStatus.Disabled);
            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserStatusChangedEvent>>()), Times.Once);
        }

        // ---------- ActivateUserAsync ----------

        [Fact]
        public async Task Activate_MissingUserId_ReturnsError()
        {
            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "", Reason = "valid reason" });
            result.Errors.Should().ContainKey(nameof(ActivateUserByAdminRequest.UserId));
        }

        [Fact]
        public async Task Activate_ShortReason_ReturnsError()
        {
            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "u1", Reason = "hi" });
            result.Errors.Should().ContainKey(nameof(ActivateUserByAdminRequest.Reason));
        }

        [Fact]
        public async Task Activate_UserNotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);
            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "u1", Reason = "reinstate now" });
            result.Errors.Should().ContainKey(nameof(ActivateUserByAdminRequest.UserId));
        }

        [Fact]
        public async Task Activate_AlreadyActive_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1"))
                .ReturnsAsync(new User { ItemId = "u1", Active = true, Status = UserLifecycleStatus.Active });
            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "u1", Reason = "reinstate now" });
            result.Errors.Should().ContainKey(nameof(ActivateUserByAdminRequest.UserId));
        }

        [Fact]
        public async Task Activate_HappyPath_ActivatesAndAudits()
        {
            var user = new User { ItemId = "u1", Active = false, Status = UserLifecycleStatus.Disabled };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "u1", Reason = "reinstate now" });

            result.IsSuccess.Should().BeTrue();
            user.Active.Should().BeTrue();
            user.Status.Should().Be(UserLifecycleStatus.Active);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "USER_ACTIVATED")), Times.Once);
        }

        // ---------- ActivateAndLinkSocialIdentityAsync ----------

        [Fact]
        public async Task ActivateAndLink_MissingFields_ReturnErrors()
        {
            var svc = Create();
            (await svc.ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "", Provider = "google", ProviderUserId = "p" }))
                .Errors.Should().ContainKey("UserId");
            (await svc.ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "", ProviderUserId = "p" }))
                .Errors.Should().ContainKey("Provider");
            (await svc.ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "google", ProviderUserId = "" }))
                .Errors.Should().ContainKey("ProviderUserId");
        }

        [Fact]
        public async Task ActivateAndLink_UserNotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);
            var result = await Create().ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "google", ProviderUserId = "p" });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task ActivateAndLink_AlreadyActiveVerified_ReturnsSuccessNoChange()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1", Active = true, IsVerified = true });
            var result = await Create().ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "google", ProviderUserId = "p" });
            result.IsSuccess.Should().BeTrue();
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task ActivateAndLink_Disabled_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1", Status = UserLifecycleStatus.Disabled });
            var result = await Create().ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "google", ProviderUserId = "p" });
            result.Errors.Should().ContainKey("account_disabled");
        }

        [Fact]
        public async Task ActivateAndLink_HappyPath_LinksIdentity()
        {
            var user = new User { ItemId = "u1", Active = false, IsVerified = false, Status = UserLifecycleStatus.PendingVerification, ExternalIdentities = null! };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().ActivateAndLinkSocialIdentityAsync(new ActivateAndLinkSocialIdentityRequest { UserId = "u1", Provider = "google", ProviderUserId = "pid" });

            result.IsSuccess.Should().BeTrue();
            user.Active.Should().BeTrue();
            user.ExternalIdentities.Should().ContainSingle(e => e.Provider == "google" && e.ProviderUserId == "pid");
        }

        // ---------- UpdateUserAccessControlAsync ----------

        [Fact]
        public async Task UpdateAccessControl_NotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);
            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "u1" });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task UpdateAccessControl_OtherOrgActor_Denied()
        {
            InstallContext(orgId: "org-actor");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1" });
            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "u1", OrganizationId = "org-other" });
            result.Errors.Should().ContainKey("OrganizationId");
        }

        [Fact]
        public async Task UpdateAccessControl_OrgNotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1" });
            _resourceRepo.Setup(r => r.GetOrganizationById("org-5")).ReturnsAsync((Organization)null!);
            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "u1", OrganizationId = "org-5" });
            result.Errors.Should().ContainKey("OrganizationId");
        }

        [Fact]
        public async Task UpdateAccessControl_TooManyPermissions_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1" });
            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "u1", OrganizationId = "default",
                Permissions = new List<string> { "1", "2", "3", "4", "5", "6" }
            });
            result.Errors.Should().ContainKey("Permissions");
        }

        [Fact]
        public async Task UpdateAccessControl_AddToOrg_HappyPath()
        {
            var user = new User { ItemId = "u1", OrganizationIds = new List<string>() };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "u1", OrganizationId = "default",
                Roles = new List<string> { "admin" }, Permissions = new List<string> { "read" }
            });

            result.IsSuccess.Should().BeTrue();
            user.OrganizationIds.Should().Contain("default");
            user.Roles["default"].Should().Contain("admin");
        }

        // ---------- RevokeUserAccessControlAsync ----------

        [Fact]
        public async Task Revoke_MissingUserId_ReturnsError()
        {
            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "" });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Revoke_NotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);
            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "u1", OrganizationId = "default" });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Revoke_SelfRevoke_Denied()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("actor-1")).ReturnsAsync(new User { ItemId = "actor-1" });
            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "actor-1", OrganizationId = "default" });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Revoke_HappyPath_RemovesOrg()
        {
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = new List<string> { "default", "org-2" },
                Roles = new Dictionary<string, List<string>> { ["default"] = new() { "r" } },
                Permissions = new Dictionary<string, List<string>> { ["default"] = new() { "p" } }
            };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "u1", OrganizationId = "default" });

            result.IsSuccess.Should().BeTrue();
            user.OrganizationIds.Should().NotContain("default");
            user.Roles.Should().NotContainKey("default");
        }

        // ---------- CreateUserByEmailAsync ----------

        [Fact]
        public async Task CreateUserByEmail_HappyPath_CreatesAndDispatches()
        {
            var user = new User { ItemId = "new-id", Language = "en-US" };
            _userRepo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            _userRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 60 });
            _userRepo.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<CreateUserByEmailPostEvent>())).Returns(Task.CompletedTask);

            var ok = await Create().CreateUserByEmailAsync(new CreateUserByEmailEvent
            {
                Email = "emailed@test.com", EventType = "AccountActivation", EventQueue = "q", OrganizationId = "default"
            });

            ok.Should().BeTrue();
            _userRepo.Verify(r => r.GetUserByUserNameOrgIdAsync("emailed@test.com", "default"), Times.Once);
            _userRepo.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Once);
        }

        // ---------- CreateUserFromSsoAsync ----------

        [Fact]
        public async Task CreateUserFromSso_HappyPath_CreatesUser()
        {
            var result = await Create().CreateUserFromSsoAsync(new CreateUserViaSsoRequest
            {
                Email = " Sso@Test.com ", Platform = "google", OrganizationId = "default", ExternalUserId = "ext-1"
            });

            result.IsSuccess.Should().BeTrue();
            _userRepo.Verify(r => r.CreateUserAsync(It.Is<User>(u => u.Email == "sso@test.com" && u.UserName == "sso@test.com" && u.ProvisioningSource == UserProvisioningSource.Social)), Times.Once);
            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateUserViaSsoEvent>>()), Times.Once);
        }

        [Fact]
        public async Task ProcessSsoUser_LinksExternalIdentity_WhenExternalUserId()
        {
            var id = await Create().ProcessSsoUserAsync(new CreateUserViaSsoRequest
            {
                Email = "sso@test.com", Platform = "github", OrganizationId = "default", ExternalUserId = "gh-1"
            });
            id.Should().NotBeNullOrWhiteSpace();
            _userRepo.Verify(r => r.CreateUserAsync(It.Is<User>(u => u.ExternalIdentities.Any(e => e.ProviderUserId == "gh-1"))), Times.Once);
        }

        // ---------- Event executors ----------

        [Fact]
        public async Task ExecuteUserMutationViaSso_SendsWelcomeMail_WhenFlagged()
        {
            var user = new User { ItemId = "u1", Active = true };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            _iam.Setup(i => i.SendAccountActivationEmailAsync(user, "Welcome")).ReturnsAsync(true);

            await Create().ExecuteUserMutationViaSsoCommandAsync(new CreateUserViaSsoEvent
            {
                ItemId = "u1", Action = MutationEventType.Create, SendWelcomeMail = true, MailPurpose = "Welcome"
            });

            _iam.Verify(i => i.SendAccountActivationEmailAsync(user, "Welcome"), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteUserMutationCommand_SendsActivationAndActivity()
        {
            var user = new User { ItemId = "u1", Language = "en-US", MailPurpose = "AccountActivation" };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            _userRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                ActivationUrlLifetimeInMinutes = 30,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://app.test"
            });
            _userRepo.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iam.Setup(i => i.SendActivationToEmailAsync(user, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            await Create().ExecuteUserMutationCommandAsync(new UserMutationEvent { ItemId = "u1", Action = MutationEventType.Create });

            _iam.Verify(i => i.SendActivationToEmailAsync(user, It.Is<string>(s => s.StartsWith("https://app.test")), It.IsAny<string>()), Times.Once);
            _userRepo.Verify(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()), Times.Once);
        }
    }
}
