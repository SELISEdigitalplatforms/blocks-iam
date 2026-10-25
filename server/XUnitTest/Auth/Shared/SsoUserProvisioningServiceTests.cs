using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Blocks.Genesis.Entities.Iam;

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
            _resourceRepository.Object,
            // The real shared resolver, so placement is exercised exactly as the email sign-up sees it.
            new SignupOrganizationResolver(_resourceRepository.Object));

        /// <summary>
        /// The login services have already stamped the identity provider's InitialRoles /
        /// InitialPermissions onto the external user by the time provisioning sees it.
        /// </summary>
        private static BYOSsoUserData ExternalUser(
            string email = "New@Example.com",
            List<string>? roles = null,
            List<string>? permissions = null)
            => new()
            {
                Email = email,
                FirstName = "Ada",
                LastName = "Lovelace",
                ExternalProviderUserId = "ext-1",
                Roles = roles ?? new List<string> { "idp-role" },
                Permissions = permissions ?? new List<string> { "idp-permission" }
            };

        private void TenantConfig(
            bool ssoSignup,
            bool multiOrg = false,
            bool orgFromSignup = false,
            bool nameUniqueness = false,
            bool autoJoin = false,
            string? autoJoinTarget = null)
        {
            _resourceRepository.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration
                {
                    IsSSoSignUpEnabled = ssoSignup,
                    IsMultiOrgEnabled = multiOrg,
                    AllowOrgCreationFromSignup = orgFromSignup,
                    IsOrgNameUniquenessEnabled = nameUniqueness,
                    IsSignupAutoJoinEnabled = autoJoin,
                    SignupAutoJoinOrganizationId = autoJoinTarget,
                    // Deliberately populated: an SSO user must NOT pick these up. They describe
                    // the email-signup route, not what an identity provider grants.
                    DefaultRolesForNewUserOnSignUp = new List<string> { "signup-default-role" },
                    DefaultPermissionsForNewUserOnSignUp = new List<string> { "signup-default-permission" }
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
            created.Roles[DefaultOrganizationId].Should().Equal("idp-role");
            created.Permissions[DefaultOrganizationId].Should().Equal("idp-permission");
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
            created.Roles["org-77"].Should().Equal("idp-role");
            created.Permissions["org-77"].Should().Equal("idp-permission");

            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(
                    It.Is<CreateOrganizationRequest>(req =>
                        req.Name == "Ada Lovelace Organization" && req.CreatedFrom == CreatedFrom.ConstructSignup),
                    It.IsAny<string>()),
                Times.Once);
        }

        /// <summary>
        /// The random suffix exists only to dodge the uniqueness check. A tenant that does not
        /// enforce uniqueness must not be handed "Ada Lovelace Organization A3B9C", and the name
        /// must not even be looked up.
        /// </summary>
        [Fact]
        public async Task NewUser_UniquenessDisabled_TakenName_StillUsesTheUnsuffixedBaseName()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true, nameUniqueness: false);
            _resourceRepository.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Organization { ItemId = "existing", Name = "Ada Lovelace Organization" });
            _resourceMutation
                .Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "org-78" });

            await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(
                    It.Is<CreateOrganizationRequest>(req => req.Name == "Ada Lovelace Organization"),
                    It.IsAny<string>()),
                Times.Once);
            _resourceRepository.Verify(r => r.GetOrganizationByNameAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NewUser_UniquenessEnabled_TakenName_SuffixesTheOrganizationName()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true, nameUniqueness: true);
            _resourceRepository.Setup(r => r.GetOrganizationByNameAsync("Ada Lovelace Organization"))
                .ReturnsAsync(new Organization { ItemId = "existing", Name = "Ada Lovelace Organization" });
            _resourceRepository.Setup(r => r.GetOrganizationByNameAsync(It.Is<string>(n => n != "Ada Lovelace Organization")))
                .ReturnsAsync((Organization?)null);
            _resourceMutation
                .Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "org-79" });

            await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(
                    It.Is<CreateOrganizationRequest>(req =>
                        req.Name.StartsWith("Ada Lovelace Organization ") && req.Name != "Ada Lovelace Organization"),
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
        public async Task Roles_ComeFromTheIdentityProvider_NotTheTenantSignupDefaults()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: false);

            await Create().ResolveOrProvisionAsync(
                ExternalUser(roles: new List<string> { "idp-admin" }, permissions: new List<string> { "idp-write" }),
                "google");

            var created = CapturedUser();
            created.Roles[DefaultOrganizationId].Should().Equal("idp-admin");
            created.Permissions[DefaultOrganizationId].Should().Equal("idp-write");
            created.Roles[DefaultOrganizationId].Should().NotContain("signup-default-role");
            created.Permissions[DefaultOrganizationId].Should().NotContain("signup-default-permission");
        }

        [Fact]
        public async Task UnconfiguredIdentityProvider_GrantsNothing()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: false);

            await Create().ResolveOrProvisionAsync(
                ExternalUser(roles: new List<string>(), permissions: new List<string>()),
                "google");

            var created = CapturedUser();
            created.Roles[DefaultOrganizationId].Should().BeEmpty();
            created.Permissions[DefaultOrganizationId].Should().BeEmpty();
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

        // ---------- Sign-up auto-join ----------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task H9_H10_H11_NewUser_AutoJoinOn_JoinsTheTarget_WithIdpRoles_AndNoPersonalOrganization(bool orgFromSignup)
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: orgFromSignup, autoJoin: true, autoJoinTarget: "org-sales");
            _resourceRepository.Setup(r => r.GetOrganizationById("org-sales"))
                .ReturnsAsync(new Organization { ItemId = "org-sales", Name = "Sales" });

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);
            var created = CapturedUser();
            created.OrganizationIds.Should().Equal("org-sales");
            created.Roles.Keys.Should().Equal("org-sales");
            created.Roles["org-sales"].Should().Equal("idp-role");
            created.Permissions["org-sales"].Should().Equal("idp-permission");
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
            _resourceRepository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task H9_NewUser_AutoJoinToDefault_LandsInDefault_WithoutAnyOrganizationRead()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true, autoJoin: true, autoJoinTarget: DefaultOrganizationId);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.Provisioned);
            CapturedUser().OrganizationIds.Should().Equal(DefaultOrganizationId);
            _resourceRepository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task C8_C9_NewUser_TargetMissingOrDisabled_IsOrganizationUnavailable_AndWritesNothing(bool exists)
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: true, orgFromSignup: true, autoJoin: true, autoJoinTarget: "org-sales");
            _resourceRepository.Setup(r => r.GetOrganizationById("org-sales"))
                .ReturnsAsync(exists ? new Organization { ItemId = "org-sales", Name = "Sales", IsDisabled = true } : null!);

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.OrganizationUnavailable);
            result.User.Should().BeNull();
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
            _resourceMutation.Verify(
                r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public void ProvisioningOutcomeNumbering_AddsOrganizationUnavailableAs4_ExistingValuesUnchanged()
        {
            ((int)SsoProvisioningOutcome.ExistingUser).Should().Be(0);
            ((int)SsoProvisioningOutcome.Provisioned).Should().Be(1);
            ((int)SsoProvisioningOutcome.SignupDisabled).Should().Be(2);
            ((int)SsoProvisioningOutcome.Failed).Should().Be(3);
            ((int)SsoProvisioningOutcome.OrganizationUnavailable).Should().Be(4);
        }

        [Fact]
        public async Task C13_NewUser_SsoSignupOff_IsRefusedBeforeAnyAutoJoinLogic()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null!);
            TenantConfig(ssoSignup: false, multiOrg: true, autoJoin: true, autoJoinTarget: "org-sales");

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.SignupDisabled);
            _resourceRepository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task C10_ExistingUser_AutoJoinOn_IsALogin_AndMembershipsAreUntouched()
        {
            var existing = new User
            {
                ItemId = "u1",
                Email = "new@example.com",
                Active = true,
                OrganizationIds = ["org-other"]
            };
            _userRepository.Setup(r => r.GetUserByEmailAsync("new@example.com")).ReturnsAsync(existing);
            TenantConfig(ssoSignup: true, multiOrg: true, autoJoin: true, autoJoinTarget: "org-sales");

            var result = await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            result.Outcome.Should().Be(SsoProvisioningOutcome.ExistingUser);
            existing.OrganizationIds.Should().Equal("org-other");
            _resourceRepository.Verify(r => r.GetTenantConfigurationAsync(), Times.Never);
            _resourceRepository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
            _userRepository.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task C12_NewUser_MultiOrgOff_AutoJoinStored_StillLandsInDefault()
        {
            NoExistingUser();
            TenantConfig(ssoSignup: true, multiOrg: false, autoJoin: true, autoJoinTarget: "org-sales");

            await Create().ResolveOrProvisionAsync(ExternalUser(), "google");

            CapturedUser().OrganizationIds.Should().Equal(DefaultOrganizationId);
            _resourceRepository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }
    }
}
