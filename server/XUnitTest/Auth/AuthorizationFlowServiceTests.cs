using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    /// <summary>
    /// AuthorizationFlowService is a thin orchestrator that forwards to three endpoint services.
    /// These tests drive delegation through each forwarded method using paths that return early
    /// (before any heavy dependency is touched), proving the call is wired to the right endpoint.
    /// </summary>
    public class AuthorizationFlowServiceTests : IDisposable
    {
        public AuthorizationFlowServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        [Fact]
        public async Task TokenAsync_DelegatesToTokenEndpoint_UnsupportedGrant()
        {
            // Real OidcTokenEndpoint; the unsupported-grant branch never touches the inner issuers.
            var tokenEndpoint = new OidcTokenEndpoint(null!, null!, null!, null!, null!, NullLogger<OidcTokenEndpoint>.Instance);
            var flow = new AuthorizationFlowService(null!, null!, tokenEndpoint);

            var result = await flow.TokenAsync("totally_unsupported_grant", new DefaultHttpContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(bad.Value).Should().Contain("unsupported_grant_type");
        }

        [Fact]
        public async Task AuthorizeAsync_DelegatesToAuthorizationEndpoint_InvalidRequest()
        {
            // Real OidcAuthorizationEndpoint; invalid inputs fail validation before any dependency is used.
            var authorizationEndpoint = new OidcAuthorizationEndpoint(
                null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<OidcAuthorizationEndpoint>.Instance);
            var flow = new AuthorizationFlowService(null!, authorizationEndpoint, null!);

            var result = await flow.AuthorizeAsync(
                client_id: "",
                response_type: "",
                redirect_uri: "",
                scope: "",
                state: "",
                nonce: "",
                code_challenge: "",
                code_challenge_method: "",
                prompt: null,
                tenant_id: null,
                request: new DefaultHttpContext().Request,
                response: new DefaultHttpContext().Response,
                blocksUserId: null,
                returnRedirectResponse: false,
                mfaCompleted: false);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(bad.Value).Should().Contain("invalid_request");
        }

        [Fact]
        public async Task ExecuteOidcLoginAsync_DelegatesToLoginOrchestrator_SocialLogin()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var authService = new Mock<IAuthenticationService>();
            var sentinel = new OkObjectResult(new { redirect = "https://provider/authorize" });
            authService.Setup(s => s.GetOidcSocialAuthorizationUrlAsync("provider-1", It.IsAny<string>(), "https://cb"))
                .ReturnsAsync(sentinel);

            // Real OidcLoginOrchestrator; the social path uses only the cache + auth service.
            var orchestrator = new OidcLoginOrchestrator(
                null!, null!, authService.Object, null!, cache.Object, null!, null!, null!, null!, null!,
                NullLogger<OidcLoginOrchestrator>.Instance);
            var flow = new AuthorizationFlowService(orchestrator, null!, null!);

            var request = new OidcLoginRequest { ProviderClientId = "provider-1", ProviderRedirectUri = "https://cb" };
            var result = await flow.ExecuteOidcLoginAsync(request, new DefaultHttpContext().Request, new DefaultHttpContext().Response);

            result.Should().BeSameAs(sentinel);
            authService.Verify(s => s.GetOidcSocialAuthorizationUrlAsync("provider-1", It.IsAny<string>(), "https://cb"), Times.Once);
            cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }
    }
}
