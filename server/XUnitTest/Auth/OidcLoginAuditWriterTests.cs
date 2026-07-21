using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    public class OidcLoginAuditWriterTests
    {
        [Fact]
        public async Task WriteAsync_OnLoginSuccess_PublishesAuthCategoryAndSuccessOutcome()
        {
            var dispatcher = new Mock<IUserActivityDispatcher>();
            UserActivityEvent? captured = null;
            dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>()))
                .Returns(new DeviceInformation());

            var writer = new OidcLoginAuditWriter(
                authDomain.Object,
                dispatcher.Object,
                NullLogger<OidcLoginAuditWriter>.Instance);

            var user = new User { ItemId = "user-1" };
            var httpRequest = new DefaultHttpContext().Request;

            await writer.WriteAsync("tenant-1", "client-1", user, httpRequest, "login_success", "oidc_login_success");

            captured.Should().NotBeNull();
            captured!.Category.Should().Be(UserActivityCategory.Auth);
            captured.Event.Should().Be("login_success");
            captured.Outcome.Should().Be(IdpConstants.StatusSuccess);
            captured.Severity.Should().Be("low");
            captured.UserId.Should().Be("user-1");
            captured.TenantId.Should().Be("tenant-1");
            captured.ClientId.Should().Be("client-1");
            captured.Source.Should().Be("auth-oidc-login");
            captured.ReasonCode.Should().BeNull();
            captured.Metadata.Should().ContainKey("details").WhoseValue.Should().Be("oidc_login_success");
        }

        [Fact]
        public async Task WriteAsync_OnLoginFailure_PublishesFailureOutcomeAndReasonCode()
        {
            var dispatcher = new Mock<IUserActivityDispatcher>();
            UserActivityEvent? captured = null;
            dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>()))
                .Returns(new DeviceInformation());

            var writer = new OidcLoginAuditWriter(
                authDomain.Object,
                dispatcher.Object,
                NullLogger<OidcLoginAuditWriter>.Instance);

            var user = new User { ItemId = "user-2" };
            var httpRequest = new DefaultHttpContext().Request;

            await writer.WriteAsync(null, null, user, httpRequest, "login_failure", "invalid_credentials");

            captured.Should().NotBeNull();
            captured!.Category.Should().Be(UserActivityCategory.Auth);
            captured.Outcome.Should().Be(IdpConstants.StatusFailure);
            captured.Severity.Should().Be("medium");
            captured.ReasonCode.Should().Be("login_failure");
            captured.TenantId.Should().BeNull();
            captured.ClientId.Should().BeNull();
        }

        [Fact]
        public async Task WriteAsync_WhenDispatcherThrows_DoesNotRethrow()
        {
            var dispatcher = new Mock<IUserActivityDispatcher>();
            dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .ThrowsAsync(new InvalidOperationException("queue down"));

            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>()))
                .Returns(new DeviceInformation());

            var writer = new OidcLoginAuditWriter(
                authDomain.Object,
                dispatcher.Object,
                NullLogger<OidcLoginAuditWriter>.Instance);

            var act = async () => await writer.WriteAsync(
                "tenant-1", "client-1",
                new User { ItemId = "user-3" },
                new DefaultHttpContext().Request,
                "login_success",
                null);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task WriteAsync_AcceptsOidcLoginRequestOverload_AndDelegatesToPrimitiveOverload()
        {
            var dispatcher = new Mock<IUserActivityDispatcher>();
            UserActivityEvent? captured = null;
            dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>()))
                .Returns(new DeviceInformation());

            var writer = new OidcLoginAuditWriter(
                authDomain.Object,
                dispatcher.Object,
                NullLogger<OidcLoginAuditWriter>.Instance);

            var request = new Authentication.DomainService.Authentication.OidcLoginRequest
            {
                TenantId = "tenant-9",
                ClientId = "client-9"
            };
            var user = new User { ItemId = "user-9" };
            var httpRequest = new DefaultHttpContext().Request;

            await writer.WriteAsync(request, user, httpRequest, "login_success", null);

            captured.Should().NotBeNull();
            captured!.TenantId.Should().Be("tenant-9");
            captured.ClientId.Should().Be("client-9");
        }
    }
}
