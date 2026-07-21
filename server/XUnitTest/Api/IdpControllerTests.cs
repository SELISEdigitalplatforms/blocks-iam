using Api.Controllers;
using Authentication.DomainService.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="IdpController"/>. The IDP service is mocked; each action asserts
    /// that the controller returns the delegated <see cref="IActionResult"/> unchanged.
    /// </summary>
    public class IdpControllerTests
    {
        private readonly Mock<IIdpService> _idpService = new();

        private IdpController CreateController()
        {
            var controller = new IdpController(_idpService.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return controller;
        }

        [Fact]
        public async Task InitiateAuthenticationFlow_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("initiated");
            _idpService.Setup(s => s.StartAuthenticationFlowAsync("client", "https://cb", "fwd"))
                .ReturnsAsync(sentinel);

            var result = await CreateController().InitiateAuthenticationFlow("client", "https://cb", "fwd");

            result.Should().BeSameAs(sentinel);
        }

        [Fact]
        public async Task Callback_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("callback");
            _idpService.Setup(s => s.HandleCallbackAsync(
                    "code", "state", null, null, It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().Callback("code", "state", null, null);

            result.Should().BeSameAs(sentinel);
        }

        [Fact]
        public async Task OidcUiConfig_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("ui-config");
            _idpService.Setup(s => s.GetUiConfigAsync()).ReturnsAsync(sentinel);

            var result = await CreateController().OidcUiConfig();

            result.Should().BeSameAs(sentinel);
        }
    }
}
