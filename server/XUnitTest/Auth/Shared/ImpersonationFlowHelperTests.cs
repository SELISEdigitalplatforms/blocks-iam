using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    public class ImpersonationFlowHelperTests
    {
        [Fact]
        public async Task CreateAndBackupImpersonationSessionAsync_PersistsAndReturnsId()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.InsertImpersonationSessionAsync(It.IsAny<ImpersonationSession>())).ReturnsAsync(true);

            var helper = new ImpersonationFlowHelper(repo.Object);

            var sessionId = await helper.CreateAndBackupImpersonationSessionAsync(
                "user-1", "root-tenant", "target-tenant", "client-1", "org-1");

            sessionId.Should().NotBeNullOrEmpty();
            repo.Verify(r => r.InsertImpersonationSessionAsync(It.Is<ImpersonationSession>(s =>
                s.UserId == "user-1" &&
                s.RootTenantId == "root-tenant" &&
                s.TargetTenantId == "target-tenant" &&
                s.ClientId == "client-1" &&
                s.OrganizationId == "org-1" &&
                s.Status == "active"
            )), Times.Once);
        }

        [Fact]
        public async Task CreateAndBackupImpersonationSessionAsync_DefaultsOrganizationToDefault_WhenNull()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.InsertImpersonationSessionAsync(It.IsAny<ImpersonationSession>())).ReturnsAsync(true);

            var helper = new ImpersonationFlowHelper(repo.Object);

            await helper.CreateAndBackupImpersonationSessionAsync(
                "u", "root", "target", "c", null);

            repo.Verify(r => r.InsertImpersonationSessionAsync(It.Is<ImpersonationSession>(s => s.OrganizationId == "default")), Times.Once);
        }

        [Fact]
        public async Task CreateAndBackupImpersonationSessionAsync_Throws_OnFailure()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.InsertImpersonationSessionAsync(It.IsAny<ImpersonationSession>())).ReturnsAsync(false);

            var helper = new ImpersonationFlowHelper(repo.Object);

            var act = async () => await helper.CreateAndBackupImpersonationSessionAsync("u", "r", "t", "c", null);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task SwitchOrganizationContextAsync_ReturnsFalse_WhenSessionNotFound()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1")).ReturnsAsync((ImpersonationSession?)null);

            var helper = new ImpersonationFlowHelper(repo.Object);

            var result = await helper.SwitchOrganizationContextAsync("imp-1", "org-2");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SwitchOrganizationContextAsync_ReturnsFalse_WhenSessionNotActive()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Status = "inactive" });

            var helper = new ImpersonationFlowHelper(repo.Object);

            var result = await helper.SwitchOrganizationContextAsync("imp-1", "org-2");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SwitchOrganizationContextAsync_UpdatesSession_WhenActive()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Status = "active" });
            repo.Setup(r => r.UpdateImpersonationSessionAsync("imp-1", It.IsAny<Dictionary<string, object>>())).ReturnsAsync(true);

            var helper = new ImpersonationFlowHelper(repo.Object);

            var result = await helper.SwitchOrganizationContextAsync("imp-1", "org-2");

            result.Should().BeTrue();
            repo.Verify(r => r.UpdateImpersonationSessionAsync("imp-1", It.IsAny<Dictionary<string, object>>()), Times.Once);
        }
    }
}