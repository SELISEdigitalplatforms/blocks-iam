using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Services;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    public class AuthSessionFacadeTests
    {
        [Fact]
        public async Task CreateSessionAsync_DelegatesToIdpSessionService()
        {
            var idpSession = new Mock<IIdpSessionService>();
            var impersonation = new Mock<IImpersonationFlowHelper>();
            var revocation = new Mock<ITokenRevocationService>();
            var jwt = new Mock<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>();

            idpSession.Setup(s => s.CreateSessionAsync("u", "t", "ip")).ReturnsAsync("session-1");

            var facade = new AuthSessionFacade(
                idpSession.Object,
                impersonation.Object,
                revocation.Object,
                null!,
                jwt.Object,
                new Mock<IRefreshSessionResolver>().Object);

            var result = await facade.CreateSessionAsync("u", "t", "ip");

            result.Should().Be("session-1");
            idpSession.Verify(s => s.CreateSessionAsync("u", "t", "ip"), Times.Once);
        }

        [Fact]
        public async Task GetSessionAsync_DelegatesToIdpSessionService()
        {
            var idpSession = new Mock<IIdpSessionService>();
            var expected = new Idp.DomainService.Oidc.Contracts.IdpSessionModel { SessionId = "s1" };
            idpSession.Setup(s => s.GetSessionAsync("s1")).ReturnsAsync(expected);

            var facade = new AuthSessionFacade(
                idpSession.Object,
                Mock.Of<IImpersonationFlowHelper>(),
                Mock.Of<ITokenRevocationService>(),
                null!,
                Mock.Of<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>(),
                Mock.Of<IRefreshSessionResolver>());

            var result = await facade.GetSessionAsync("s1");

            result.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task AddAccountAsync_DelegatesToIdpSessionService()
        {
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.AddAccountAsync("s1", "u", "t", "name")).ReturnsAsync(true);

            var facade = new AuthSessionFacade(
                idpSession.Object,
                Mock.Of<IImpersonationFlowHelper>(),
                Mock.Of<ITokenRevocationService>(),
                null!,
                Mock.Of<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>(),
                Mock.Of<IRefreshSessionResolver>());

            var result = await facade.AddAccountAsync("s1", "u", "t", "name");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateActivityAsync_DelegatesToIdpSessionService()
        {
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.UpdateActivityAsync("s1")).ReturnsAsync(true);

            var facade = new AuthSessionFacade(
                idpSession.Object,
                Mock.Of<IImpersonationFlowHelper>(),
                Mock.Of<ITokenRevocationService>(),
                null!,
                Mock.Of<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>(),
                Mock.Of<IRefreshSessionResolver>());

            var result = await facade.UpdateActivityAsync("s1");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task CreateAndBackupImpersonationSession_DelegatesToImpersonationFlowHelper()
        {
            var impersonation = new Mock<IImpersonationFlowHelper>();
            impersonation.Setup(i => i.CreateAndBackupImpersonationSessionAsync("u", "root", "target", "client", "org"))
                .ReturnsAsync("imp-1");

            var facade = new AuthSessionFacade(
                Mock.Of<IIdpSessionService>(),
                impersonation.Object,
                Mock.Of<ITokenRevocationService>(),
                null!,
                Mock.Of<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>(),
                Mock.Of<IRefreshSessionResolver>());

            var result = await facade.CreateAndBackupImpersonationSessionAsync("u", "root", "target", "client", "org");

            result.Should().Be("imp-1");
        }

        [Fact]
        public async Task RevokeTokenAsync_DelegatesToRevocationService()
        {
            var revocation = new Mock<ITokenRevocationService>();
            var expected = new global::Authentication.DomainService.Oidc.Repositories.TokenRevocationResult { Success = true };
            revocation.Setup(r => r.RevokeTokenAsync("token", "refresh_token", "client"))
                .ReturnsAsync(expected);

            var facade = new AuthSessionFacade(
                Mock.Of<IIdpSessionService>(),
                Mock.Of<IImpersonationFlowHelper>(),
                revocation.Object,
                null!,
                Mock.Of<global::Authentication.DomainService.OAuth.IOAuthJwtAccessTokenManager>(),
                Mock.Of<IRefreshSessionResolver>());

            var result = await facade.RevokeTokenAsync("token", "refresh_token", "client");

            result.Should().BeSameAs(expected);
        }
    }
}