using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Moq;

namespace XUnitTest.IamTests.Accounts
{
    public class LoginPolicySettingsServiceTests
    {
        private readonly Mock<IIdentityAccessManagementRepository> _repo = new();

        private LoginPolicySettingsService Create() => new(_repo.Object);

        [Fact]
        public async Task Get_NeverSaved_ReturnsEverythingOn()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new IamTenantConfiguration());

            var result = await Create().GetAsync();

            result.IsSuccess.Should().BeTrue();
            result.LoginPolicy.Should().BeEquivalentTo(new TenantLoginPolicy());
        }

        [Fact]
        public async Task Get_ReturnsTheStoredPolicy()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { LoginPolicy = new TenantLoginPolicy { AllowSocial = false } });

            (await Create().GetAsync()).LoginPolicy.AllowSocial.Should().BeFalse();
        }

        [Fact]
        public async Task Save_StoresAllFourSwitches()
        {
            var request = new TenantLoginPolicy { AllowEmbedded = false, AllowOidc = true, AllowPassword = true, AllowSocial = false };

            var result = await Create().SaveAsync(request);

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveLoginPolicyAsync(
                It.Is<TenantLoginPolicy>(p => !p.AllowEmbedded && p.AllowOidc && p.AllowPassword && !p.AllowSocial),
                It.IsAny<string?>()), Times.Once);
        }

        [Theory]
        [InlineData(false, false, true, true)]
        [InlineData(true, true, false, false)]
        public async Task Save_PolicyThatLetsNobodySignIn_IsRefused(bool embedded, bool oidc, bool password, bool social)
        {
            var request = new TenantLoginPolicy { AllowEmbedded = embedded, AllowOidc = oidc, AllowPassword = password, AllowSocial = social };

            var result = await Create().SaveAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(LoginPolicySettingsService.LocksEveryoneError);
            _repo.Verify(r => r.SaveLoginPolicyAsync(It.IsAny<TenantLoginPolicy>(), It.IsAny<string?>()), Times.Never);
        }
    }
}
