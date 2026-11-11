using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 §6.5: <c>AllowNoOrgSignInForDisabledOrganizations</c> on <see cref="ResourceMutationService"/>.
    /// H6 (save true/false), C8 (null keeps the stored value, entity default false), C7 (admin read only).
    /// </summary>
    public sealed class AllowNoOrgSignInConfigTests : IDisposable
    {
        private const string Key = "allowNoOrgSignInForDisabledOrganizations";

        private readonly Mock<IResourceRepository> _repo = new();

        public AllowNoOrgSignInConfigTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
            _repo.Setup(r => r.SaveOrganizationConfig(It.IsAny<IamTenantConfiguration>())).Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private ResourceMutationService Create()
        {
            var activity = new Mock<IUserActivityDispatcher>();
            activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            var permissionValidator = new Mock<IValidator<CreatePermissionRequest>>();
            permissionValidator.Setup(v => v.ValidateAsync(It.IsAny<CreatePermissionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            return new ResourceMutationService(NullLogger<ResourceMutationService>.Instance, _repo.Object, new Mock<IIdentityAccessManagementService>().Object,
                permissionValidator.Object, new Mock<IValidator<UpdatePermissionRequest>>().Object, new Mock<IValidator<CreateRoleRequest>>().Object,
                new Mock<ITenantPermissionPropagator>().Object, activity.Object, new Mock<IUserRepository>().Object);
        }

        private void Stored(IamTenantConfiguration? config) =>
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(config!);

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task H6_Save_TrueOrFalse_IsStored(bool stored, bool sent)
        {
            Stored(new IamTenantConfiguration { AllowNoOrgSignInForDisabledOrganizations = stored });

            var result = await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest { AllowNoOrgSignInForDisabledOrganizations = sent });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveOrganizationConfig(It.Is<IamTenantConfiguration>(c => c.AllowNoOrgSignInForDisabledOrganizations == sent)), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task C8_Example9_SaveWithoutTheField_KeepsTheStoredValue(bool stored)
        {
            Stored(new IamTenantConfiguration { AllowNoOrgSignInForDisabledOrganizations = stored });

            await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest { AllowOrgCreationFromCloud = true });

            _repo.Verify(r => r.SaveOrganizationConfig(It.Is<IamTenantConfiguration>(c =>
                c.AllowNoOrgSignInForDisabledOrganizations == stored && c.AllowOrgCreationFromCloud)), Times.Once);
        }

        [Fact]
        public async Task C8_NoStoredDocument_SaveWithoutTheField_IsFalse()
        {
            Stored(null);

            await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest { AllowOrgCreationFromCloud = true });

            _repo.Verify(r => r.SaveOrganizationConfig(It.Is<IamTenantConfiguration>(c => !c.AllowNoOrgSignInForDisabledOrganizations)), Times.Once);
        }

        [Fact]
        public void C8_EntityDefault_IsFalse()
        {
            new IamTenantConfiguration().AllowNoOrgSignInForDisabledOrganizations.Should().BeFalse();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task H6_AdminRead_ReturnsTheStoredValue(bool stored)
        {
            Stored(new IamTenantConfiguration { ItemId = "cfg-1", IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = stored });

            var result = await Create().GetAdminOrganizationConfigAsync();

            result.Should().ContainKey(Key).WhoseValue.Should().Be(stored);
        }

        [Fact]
        public async Task H6_AdminRead_NoDocument_IsFalse()
        {
            Stored(null);

            (await Create().GetAdminOrganizationConfigAsync()).Should().ContainKey(Key).WhoseValue.Should().Be(false);
        }

        [Fact]
        public async Task C7_AnonymousRead_NeverCarriesTheKey()
        {
            Stored(new IamTenantConfiguration { ItemId = "cfg-1", IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = true });

            var result = await Create().GetOrganizationConfigAsync();

            result.Keys.Should().NotContain(k => string.Equals(k, Key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
