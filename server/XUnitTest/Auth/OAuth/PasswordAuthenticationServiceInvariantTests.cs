using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// Regression coverage for the password-auth invariant:
    /// "All login failure modes (user not found, inactive, not verified) MUST return
    /// the generic <c>OAuthError.InValidResponse</c> shape. Do not leak discriminators."
    /// See <see cref="PasswordAuthenticationService.AuthenticateAsync"/>.
    /// </summary>
    public class PasswordAuthenticationServiceInvariantTests
    {
        private static PasswordAuthenticationService CreateService() =>
            new(
                NullLogger<PasswordAuthenticationService>.Instance,
                new Mock<IOAuthJwtAccessTokenManager>().Object,
                new Mock<ITenants>().Object,
                new Mock<ICryptoService>().Object,
                new Mock<IAuthenticationRepository>().Object,
                new Mock<IAuthenticationDomainService>().Object,
                new Mock<IAccountService>().Object);

        private static TokenRequest BuildRequest() => new()
        {
            GrantType = "password",
            Username = "u@example.com",
            Password = "Pa$$w0rd!"
        };

        [Fact]
        public async Task AuthenticateAsync_UserIsNull_ReturnsGenericInvalidUsernamePassword()
        {
            var sut = CreateService();

            var result = await sut.AuthenticateAsync(BuildRequest(), new IdentityConfiguration(), user: null);

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task AuthenticateAsync_UserInactive_ReturnsGenericInvalidUsernamePassword()
        {
            var sut = CreateService();
            var user = new User { ItemId = "u-1", Active = false, IsVerified = true };

            var result = await sut.AuthenticateAsync(BuildRequest(), new IdentityConfiguration(), user);

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task AuthenticateAsync_UserNotVerified_ReturnsGenericInvalidUsernamePassword()
        {
            var sut = CreateService();
            var user = new User { ItemId = "u-1", Active = true, IsVerified = false };

            var result = await sut.AuthenticateAsync(BuildRequest(), new IdentityConfiguration(), user);

            result.Error.Should().Be(OAuthError.InValidUseNamePassword);
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task AuthenticateAsync_AllFailureModes_ShareIdenticalResponseShape()
        {
            var sut = CreateService();
            var request = BuildRequest();

            var nullUser = await sut.AuthenticateAsync(request, new IdentityConfiguration(), user: null);
            var inactiveUser = await sut.AuthenticateAsync(request, new IdentityConfiguration(),
                new User { ItemId = "u-1", Active = false, IsVerified = true });
            var unverifiedUser = await sut.AuthenticateAsync(request, new IdentityConfiguration(),
                new User { ItemId = "u-1", Active = true, IsVerified = false });

            // Each branch must produce the same observable error contract so the client
            // cannot distinguish between "no such user", "inactive", and "not verified".
            nullUser.Error.Should().Be(inactiveUser.Error).And.Be(unverifiedUser.Error);
            nullUser.StatusCode.Should().Be(inactiveUser.StatusCode).And.Be(unverifiedUser.StatusCode);
        }
    }
}
