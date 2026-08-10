using Api.Controllers;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="DeviceController"/> (RFC 8628 browser-facing device flow). The
    /// controller depends on the concrete <see cref="DeviceVerificationService"/>, which is built
    /// from mocked repositories so the reachable validation branches can be exercised
    /// deterministically (repository returns null -> invalid_grant).
    /// </summary>
    public class DeviceControllerTests
    {
        private readonly Mock<IDeviceAuthorizationRepository> _deviceRepo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();

        private DeviceController CreateController()
        {
            var verification = new DeviceVerificationService(
                _deviceRepo.Object,
                _sessionRepo.Object,
                _authRepo.Object,
                Options.Create(new DeviceFlowOptions()),
                NullLogger<DeviceVerificationService>.Instance);

            var controller = new DeviceController(verification);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return controller;
        }

        // ---------- Entry ----------

        [Fact]
        public void Entry_ReturnsOkWithRedirect()
        {
            var result = CreateController().Entry();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- Verify ----------

        [Fact]
        public async Task Verify_MissingUserCode_ReturnsBadRequest()
        {
            var result = await CreateController().Verify(new DeviceVerifyRequest { UserCode = "" }, CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify_UnknownUserCode_ReturnsBadRequest()
        {
            // Repository returns null (default) -> invalid_grant.
            var result = await CreateController().Verify(new DeviceVerifyRequest { UserCode = "ABCD-1234" }, CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            _deviceRepo.Verify(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- Decision ----------

        [Fact]
        public async Task Decision_MissingUserCode_ReturnsBadRequest()
        {
            var result = await CreateController().Decision(new DeviceDecisionRequest { UserCode = "", Decision = "allow" }, CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Decision_UnknownUserCode_Returns400ObjectResult()
        {
            var result = await CreateController().Decision(new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow" }, CancellationToken.None);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(400);
        }
    }
}
