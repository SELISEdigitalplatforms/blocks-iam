using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Unit tests for <see cref="UserManagementMutationService.RevokeUserAccessControlAsync"/>. Every
    /// validation guard (missing user id, unknown user, self-revocation, cross-org revocation, unknown
    /// organization), the repository-failure path and the successful revocation are covered.
    /// </summary>
    public sealed class UserManagementMutationServiceRevokeTests : IDisposable
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

        public UserManagementMutationServiceRevokeTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        }

        private void InstallContext(string userId = "actor-1", string orgId = "default")
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

        private static User TargetUser() => new()
        {
            ItemId = "target-1",
            OrganizationIds = new List<string> { "default", "org-x" },
            Roles = new() { { "org-x", new List<string> { "admin" } } },
            Permissions = new() { { "org-x", new List<string> { "read" } } }
        };

        [Fact]
        public async Task Revoke_MissingUserId_ReturnsValidationError()
        {
            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = " " });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Revoke_UserNotFound_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync((User)null!);
            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "target-1" });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Revoke_SelfRevocation_IsRejected()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("actor-1")).ReturnsAsync(new User { ItemId = "actor-1" });
            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "actor-1", OrganizationId = "default" });
            result.IsSuccess.Should().BeFalse();
            result.Errors!.Values.Should().Contain("You cannot revoke your own access");
        }

        [Fact]
        public async Task Revoke_CrossOrgActor_IsRejected()
        {
            InstallContext(orgId: "org-a");
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(TargetUser());
            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-b" });
            result.IsSuccess.Should().BeFalse();
            result.Errors!.Values.Should().Contain("Other org user can not revoke");
        }

        [Fact]
        public async Task Revoke_UnknownOrganization_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(TargetUser());
            _resourceRepo.Setup(r => r.GetOrganizationById("org-x")).ReturnsAsync((Organization)null!);
            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });
            result.IsSuccess.Should().BeFalse();
            result.Errors!.Values.Should().Contain("Organization not found");
        }

        [Fact]
        public async Task Revoke_RepositoryFailure_ReturnsError()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(TargetUser());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);
            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "default" });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Repository");
        }

        [Fact]
        public async Task Revoke_Success_RemovesOrgMembershipAndUpdates()
        {
            var user = TargetUser();
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(user);
            _resourceRepo.Setup(r => r.GetOrganizationById("org-x"))
                .ReturnsAsync(new Organization { ItemId = "org-x", Name = "Org X" });

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("target-1");
            user.OrganizationIds.Should().NotContain("org-x");
            user.Roles.Should().NotContainKey("org-x");
            user.Permissions.Should().NotContainKey("org-x");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }
    }
}
