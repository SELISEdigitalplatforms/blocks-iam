using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class SSOConsentAuthenticationServiceTests
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IUserManagementMutationService> _userMutation = new();

        private SSOConsentAuthenticationService Create() =>
            new(_cache.Object, _authRepo.Object, _tokenManager.Object, _userMutation.Object);

        private static TokenRequest Request(string code = "sso-code") => new() { Code = code };

        [Fact]
        public async Task Code_NotInCache_ReturnsInvalidCode()
        {
            _cache.Setup(c => c.KeyExistsAsync("sso-code")).ReturnsAsync(false);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_code");
            result.ErrorDescription.Should().Contain("invalid or has expired");
        }

        [Fact]
        public async Task InvalidSsoSessionData_ReturnsInvalidRequest()
        {
            _cache.Setup(c => c.KeyExistsAsync("sso-code")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("sso-code")).ReturnsAsync("null");

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task ValidSession_CreatesUser_AndReturnsToken()
        {
            var ssoUser = new CreateUserViaSsoRequest { Email = "sso@user.com", Platform = "google" };
            _cache.Setup(c => c.KeyExistsAsync("sso-code")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("sso-code")).ReturnsAsync(JsonSerializer.Serialize(ssoUser));

            _userMutation.Setup(u => u.CreateUserFromSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "user-42", IsSuccess = true });

            var createdUser = new User { ItemId = "user-42", Email = "sso@user.com", Active = true };
            _authRepo.Setup(r => r.GetUserByIdAsync("user-42")).ReturnsAsync(createdUser);

            _tokenManager.Setup(t => t.ManageTokenAsync(
                    It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "access-token" });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.AccessToken.Should().Be("access-token");
            _authRepo.Verify(r => r.GetUserByIdAsync("user-42"), Times.Once);
            _tokenManager.Verify(t => t.ManageTokenAsync(
                It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()), Times.Once);
        }

        [Fact]
        public async Task ValidSession_NullItemId_LooksUpUserByEmptyId()
        {
            var ssoUser = new CreateUserViaSsoRequest { Email = "sso@user.com", Platform = "google" };
            _cache.Setup(c => c.KeyExistsAsync("sso-code")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("sso-code")).ReturnsAsync(JsonSerializer.Serialize(ssoUser));

            _userMutation.Setup(u => u.CreateUserFromSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = null, IsSuccess = false });

            _authRepo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

            _tokenManager.Setup(t => t.ManageTokenAsync(
                    It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(new TokenResponse { Error = "user_not_found" });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("user_not_found");
            _authRepo.Verify(r => r.GetUserByIdAsync(""), Times.Once);
        }
    }
}
