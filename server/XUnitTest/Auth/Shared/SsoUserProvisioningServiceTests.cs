using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// One row of the decision table per test: the tenant's two organization switches and the
    /// SSO signup gate decide whether a user is created at all, and where they land.
    /// </summary>
    public class SsoUserProvisioningServiceTests
    {
        private const string DefaultOrganizationId = "default";

        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<IResourceMutationService> _resourceMutation = new();
        private readonly Mock<IResourceRepository> _resourceRepository = new();

        private SsoUserProvisioningService Create() => new(
            NullLogger<SsoUserProvisioningService>.Instance,
            _userRepository.Object,
            _resourceMutation.Object,
            _resourceRepository.Object);

        private static BYOSsoUserData ExternalUser(string email = "New@Example.com")
            => new() { Email = email, FirstName = "Ada", LastName = "Lovelace", ExternalProviderUserId = "ext-1" };

        private void TenantConfig(bool ssoSignup, bool multiOrg = false, bool orgFromSignup = false)
        {
            _resourceRepository.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration
                {
                    IsSSoSignUpEnabled = ssoSignup,
                    IsMultiOrgEnabled = multiOrg,
                    AllowOrgCreationFromSignup = orgFromSignup,
                    DefaultRolesForNewUserOnSignUp = new List<string> { "member" },
                    DefaultPermissionsForNewUserOnSignUp = new List<string> { "read" }
                });
        }

        private User? _createdUser;

        private void NoExistingUser()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepository.Setup(r => r.CreateUserAsync(It.IsAny<User>()))
                .Callback<User>(user => _createdUser = user)
                .ReturnsAsync(true);
        }

        private User CapturedUser()
        {
            _createdUser.Should().NotBeNull();
            return _createdUser!;
        }

        [Fact]
        public async Task ExistingUser_IsALogin_AndNoOrganizationIsCreated()
        {
            var existing = new User { ItemId = "u1", Email = "new@example.com", Active = true };
            _userRepository.Setup(r => r.GetUserByEmailAsync("new@example.com")).ReturnsAsync(existing);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.ExistingUser);
            result.User.Should().BeSameAs(existing);
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task NewUser_SignupDisabled_IsRefused()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            TenantConfig(ssoSignup: false);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.SignupDisabled);
            result.User.Should().BeNull();
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task NewUser_MultiOrgOff_LandsInDefaultOrganization()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: false);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);

            var created = CapturedUser();
            created.OrganizationIds.Should().Equal(DefaultOrganizationId);
            created.Roles.Should().ContainKey(DefaultOrganizationId);
            created.Permissions.Should().ContainKey(DefaultOrganizationId);
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task NewUser_MultiOrgOn_OrgCreationDisabled_GetsNoOrganization()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: false);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);

            var created = CapturedUser();
            created.OrganizationIds.Should().BeEmpty();
            created.Roles.Should().BeEmpty();
            created.Permissions.Should().BeEmpty();
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task NewUser_MultiOrgOn_OrgCreationAllowed_GetsANewOrganization()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true);
            _resourceRepository.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>())).ReturnsAsync((Organization?)null);
            _resourceMutation
                .Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "org-77" });

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);

            var created = CapturedUser();
            created.OrganizationIds.Should().Equal("org-77");
            created.Roles["org-77"].Should().Equal("member");
            created.Permissions["org-77"].Should().Equal("read");

            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(
                    It.Is<CreateOrganizationRequest>(req =>
                        req.Name == "Ada Lovelace Organization" && req.CreatedFrom == CreatedFrom.ConstructSignup),
                    It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task NewUser_OrgCreationFails_GetsNoOrganization_RatherThanDefault()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true);
            _resourceRepository.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>())).ReturnsAsync((Organization?)null);
            _resourceMutation
                .Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);
            CapturedUser().OrganizationIds.Should().BeEmpty();
        }

        [Fact]
        public async Task NewUser_EmailAppearsMidFlight_ReusesItInsteadOfCreatingADuplicate()
        {
            var raced = new User { ItemId = "raced", Email = "new@example.com", Active = true };

            // Absent on the first lookup, present by the time the write is attempted.
            _userRepository.SetupSequence(r => r.GetUserByEmailAsync("new@example.com"))
                .ReturnsAsync((User?)null)
                .ReturnsAsync(raced);
            TenantConfig(ssoSignup: true, multiOrg: false);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.ExistingUser);
            result.User.Should().BeSameAs(raced);
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task ProviderWithoutEmail_Fails()
        {
            var result = await Create().ResolveOrProvisionAsync(new BYOSsoUserData { Email = "" }, "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Failed);
            _userRepository.Verify(r => r.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task FailedWrite_ReportsFailure()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepository.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(false);
            TenantConfig(ssoSignup: true, multiOrg: false);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Failed);
            result.User.Should().BeNull();
        }

        [Fact]
        public async Task ProvisionedUser_IsActiveVerifiedAndNormalised()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: false);

            await Create().ResolveOrProvisionAsync(ExternalUser("New@Example.com"), "google");

            var created = CapturedUser();
            created.Email.Should().Be("new@example.com");
            created.UserName.Should().Be("new@example.com");
            created.Active.Should().BeTrue();
            created.IsVerified.Should().BeTrue();
            created.Status.Should().Be(UserLifecycleStatus.Active);
            created.ProvisioningSource.Should().Be(UserProvisioningSource.Social);
            created.Platform.Should().Be("google");
        }
    }
}
