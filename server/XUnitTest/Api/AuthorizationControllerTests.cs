using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Services;
using Blocks.Api.Controllers;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="AuthorizationController"/> (OAuth/OIDC authorization endpoints).
    /// The flow service, callback handler and authentication service are mocked. The concrete
    /// <see cref="DeviceAuthorizationEndpoint"/> is built from mocked dependencies so the reachable
    /// controller branches around it can be exercised.
    /// </summary>
    public class AuthorizationControllerTests
    {
        private readonly Mock<IAuthorizationFlowService> _flowService = new();
        private readonly Mock<IOidcCallbackHandler> _callbackHandler = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IDeviceAuthorizationService> _deviceAuthService = new();

        private DeviceAuthorizationEndpoint BuildDeviceEndpoint()
        {
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            return new DeviceAuthorizationEndpoint(
                _deviceAuthService.Object,
                accessor,
                NullLogger<DeviceAuthorizationEndpoint>.Instance);
        }

        private AuthorizationController CreateController(DefaultHttpContext? httpContext = null)
        {
            var controller = new AuthorizationController(
                _flowService.Object,
                _callbackHandler.Object,
                _authService.Object,
                BuildDeviceEndpoint());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            };
            return controller;
        }

        // ---------- OidcLogin ----------

        [Fact]
        public async Task OidcLogin_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("logged-in");
            _flowService.Setup(f => f.ExecuteOidcLoginAsync(It.IsAny<OidcLoginRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().OidcLogin(new OidcLoginRequest());

            result.Should().BeSameAs(sentinel);
        }

        // ---------- Authorize ----------

        [Fact]
        public async Task Authorize_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("authorized");
            _flowService.Setup(f => f.AuthorizeAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>(),
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().Authorize(
                "client", "code", "https://cb", "openid", "state", null, null);

            result.Should().BeSameAs(sentinel);
        }

        // ---------- Token ----------

        [Fact]
        public async Task Token_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("token");
            _flowService.Setup(f => f.TokenAsync("authorization_code", It.IsAny<HttpRequest>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().Token("authorization_code");

            result.Should().BeSameAs(sentinel);
        }

        // ---------- DeviceAuthorization ----------

        [Fact]
        public async Task DeviceAuthorization_NonPost_ReturnsBadRequest()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";

            var result = await CreateController(httpContext).DeviceAuthorization(CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task DeviceAuthorization_PostWithForm_ReturnsOk()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "POST";
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["client_id"] = "cid",
                ["scope"] = "openid"
            });
            _deviceAuthService.Setup(s => s.RequestAsync(It.IsAny<DeviceAuthorizationRequest>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeviceAuthorizationResponse());

            var result = await CreateController(httpContext).DeviceAuthorization(CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- HandleOidcCallbackGet ----------

        [Fact]
        public async Task HandleOidcCallback_MissingCode_ReturnsBadRequest()
        {
            var result = await CreateController().HandleOidcCallbackGet("", "state");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task HandleOidcCallback_MissingState_ReturnsBadRequest()
        {
            var result = await CreateController().HandleOidcCallbackGet("code", "");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task HandleOidcCallback_BodyOverridesQuery_UsesBodyValues()
        {
            _callbackHandler.Setup(h => h.HandleCallbackAsync("body-code", "body-state"))
                .ReturnsAsync(new OidcCallbackResult { IsSuccess = false, ErrorMessage = "nope" });

            var result = await CreateController().HandleOidcCallbackGet(
                "query-code", "query-state",
                new OidcCallbackRequest { Code = "body-code", State = "body-state" });

            result.Should().BeOfType<BadRequestObjectResult>();
            _callbackHandler.Verify(h => h.HandleCallbackAsync("body-code", "body-state"), Times.Once);
        }

        [Fact]
        public async Task HandleOidcCallback_HandlerFails_ReturnsBadRequest()
        {
            _callbackHandler.Setup(h => h.HandleCallbackAsync("code", "state"))
                .ReturnsAsync(new OidcCallbackResult { IsSuccess = false, ErrorMessage = "token_exchange_failed" });

            var result = await CreateController().HandleOidcCallbackGet("code", "state");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task HandleOidcCallback_HandlerSucceeds_DelegatesToAuthorize()
        {
            _callbackHandler.Setup(h => h.HandleCallbackAsync("code", "state"))
                .ReturnsAsync(new OidcCallbackResult
                {
                    IsSuccess = true,
                    ClientId = "client",
                    RedirectUri = "https://cb",
                    TenantId = "tenant-1"
                });
            var sentinel = new OkObjectResult("authorized");
            _flowService.Setup(f => f.AuthorizeAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>(),
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().HandleOidcCallbackGet("code", "state");

            result.Should().BeSameAs(sentinel);
        }
    }
}
