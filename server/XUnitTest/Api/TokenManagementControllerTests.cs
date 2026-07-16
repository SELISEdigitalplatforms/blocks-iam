using System.Security.Claims;
using System.Text;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Api.Controllers;
using FluentAssertions;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="TokenManagementController"/> (RFC 7009 revoke / RFC 7662 introspect).
    /// The client authentication and revocation services are mocked; each test asserts the resulting
    /// <see cref="IActionResult"/> for the branch under test.
    /// </summary>
    public class TokenManagementControllerTests
    {
        private const string ClientId = "cid";
        private const string ClientSecret = "sec";

        private readonly Mock<ITokenRevocationService> _revocation = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IAuthenticationDomainService> _domainService = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();

        public TokenManagementControllerTests()
        {
            // PublishTimelineAsync joins this enumerable, so it must never be null.
            _domainService.Setup(d => d.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new List<string> { "127.0.0.1" });
        }

        private TokenManagementController CreateController(ClaimsPrincipal? user = null, string? authHeader = null)
        {
            var controller = new TokenManagementController(
                _revocation.Object,
                _authRepo.Object,
                _domainService.Object,
                _dispatcher.Object,
                NullLogger<TokenManagementController>.Instance);

            var httpContext = new DefaultHttpContext();
            if (user != null)
            {
                httpContext.User = user;
            }
            if (authHeader != null)
            {
                httpContext.Request.Headers.Authorization = authHeader;
            }
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        private void SetupValidClient()
        {
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(ClientId))
                .ReturnsAsync(new OidcClientRegistration { ClientId = ClientId, ClientSecret = ClientSecret, IsActive = true });
        }

        private static ClaimsPrincipal PrincipalWithSub()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "user-1"),
                new Claim("tenant_id", "tenant-1")
            }, "test"));
        }

        // ---------- RevokeToken ----------

        [Fact]
        public async Task RevokeToken_MissingToken_ReturnsBadRequest()
        {
            var result = await CreateController().RevokeToken("", null, ClientId, ClientSecret);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RevokeToken_ClientAuthFails_ReturnsUnauthorized()
        {
            // Repository returns null -> AuthenticateClientAsync returns null.
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((OidcClientRegistration)null);

            var result = await CreateController().RevokeToken("tok", null, ClientId, ClientSecret);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task RevokeToken_ServiceReturnsInvalidClient_ReturnsUnauthorized()
        {
            SetupValidClient();
            _revocation.Setup(r => r.RevokeTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenRevocationResult { Success = false, Error = "invalid_client" });

            var result = await CreateController().RevokeToken("tok", null, ClientId, ClientSecret);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task RevokeToken_ServiceFailsOtherError_ReturnsBadRequest()
        {
            SetupValidClient();
            _revocation.Setup(r => r.RevokeTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenRevocationResult { Success = false, Error = "invalid_request" });

            var result = await CreateController().RevokeToken("tok", null, ClientId, ClientSecret);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RevokeToken_Success_NoSubClaim_ReturnsOkWithoutDispatch()
        {
            SetupValidClient();
            _revocation.Setup(r => r.RevokeTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenRevocationResult { Success = true });

            var result = await CreateController().RevokeToken("tok", "refresh_token", ClientId, ClientSecret);

            result.Should().BeOfType<OkResult>();
            // Without a "sub" claim the timeline is skipped for revocation.
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<Iam.DomainService.Dtos.UserActivityEvent>()), Times.Never);
        }

        [Fact]
        public async Task RevokeToken_Success_WithSubClaim_DispatchesTimeline()
        {
            SetupValidClient();
            _revocation.Setup(r => r.RevokeTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenRevocationResult { Success = true });

            var result = await CreateController(user: PrincipalWithSub())
                .RevokeToken("tok", "access_token", ClientId, ClientSecret);

            result.Should().BeOfType<OkResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<Iam.DomainService.Dtos.UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task RevokeToken_BasicAuthHeader_AuthenticatesAndReturnsOk()
        {
            SetupValidClient();
            _revocation.Setup(r => r.RevokeTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenRevocationResult { Success = true });

            var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            var result = await CreateController(authHeader: basic).RevokeToken("tok", null, null, null);

            result.Should().BeOfType<OkResult>();
        }

        // ---------- IntrospectToken ----------

        [Fact]
        public async Task IntrospectToken_MissingToken_ReturnsBadRequest()
        {
            var result = await CreateController().IntrospectToken("", null, ClientId, ClientSecret);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task IntrospectToken_ClientAuthFails_ReturnsUnauthorized()
        {
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((OidcClientRegistration)null);

            var result = await CreateController().IntrospectToken("tok", null, ClientId, ClientSecret);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task IntrospectToken_ActiveToken_ReturnsOkAndDispatches()
        {
            SetupValidClient();
            _revocation.Setup(r => r.IntrospectTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenIntrospectionResult { Active = true, ClientId = ClientId, Sub = "user-1" });

            var result = await CreateController().IntrospectToken("tok", "access_token", ClientId, ClientSecret);

            result.Should().BeOfType<OkObjectResult>();
            // actorId falls back to the authenticated client id, so the timeline is published.
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<Iam.DomainService.Dtos.UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task IntrospectToken_InactiveToken_ReturnsOk()
        {
            SetupValidClient();
            _revocation.Setup(r => r.IntrospectTokenAsync("tok", It.IsAny<string>(), ClientId))
                .ReturnsAsync(new TokenIntrospectionResult { Active = false });

            var result = await CreateController().IntrospectToken("tok", null, ClientId, ClientSecret);

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
