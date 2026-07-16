using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Text;

namespace XUnitTest.Auth
{
    public class ClientCredentialsTokenIssuerTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ICertificateProviderFactory> _certFactory = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();

        public ClientCredentialsTokenIssuerTests()
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

        private ClientCredentialsTokenIssuer Create()
        {
            var authService = new ClientCredentialAuthorizationService(
                _authRepo.Object, _certFactory.Object, _crypto.Object, _cache.Object, _tenants.Object);
            return new ClientCredentialsTokenIssuer(_authRepo.Object, authService);
        }

        private static HttpRequest MakeRequest(Dictionary<string, string>? form = null, string? authHeader = null)
        {
            var ctx = new DefaultHttpContext();
            var dict = (form ?? new Dictionary<string, string>())
                .ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value));
            ctx.Request.Form = new FormCollection(dict);
            if (authHeader != null)
            {
                ctx.Request.Headers["Authorization"] = authHeader;
            }
            return ctx.Request;
        }

        [Fact]
        public async Task Issue_MissingCredentials_ReturnsBadRequestInvalidClient()
        {
            var result = await Create().IssueAsync(MakeRequest());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_client", error_description = "Missing client authentication" });
        }

        [Fact]
        public async Task Issue_MalformedBasicAuthHeader_ReturnsBadRequestInvalidClient()
        {
            // No form creds and a malformed Basic header -> extraction leaves creds empty.
            var result = await Create().IssueAsync(MakeRequest(authHeader: "Basic !!!not-base64!!!"));

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Issue_AuthConfigurationMissing_ReturnsServerError()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var form = new Dictionary<string, string> { ["client_id"] = "c1", ["client_secret"] = "s1" };
            var result = await Create().IssueAsync(MakeRequest(form));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "server_error", error_description = "Authentication configuration missing" });
        }

        [Fact]
        public async Task Issue_InvalidClient_Returns401()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync((ClientCredential)null!);

            var form = new Dictionary<string, string> { ["client_id"] = "c1", ["client_secret"] = "s1" };
            var result = await Create().IssueAsync(MakeRequest(form));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Issue_BasicAuthHeader_Parsed_AndInvalidClientReturns401()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("basic-client")).ReturnsAsync((ClientCredential)null!);

            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("basic-client:basic-secret"));
            var result = await Create().IssueAsync(MakeRequest(authHeader: $"Basic {encoded}"));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            _authRepo.Verify(r => r.GetClientCredentialByIdAsync("basic-client"), Times.Once);
        }

        [Fact]
        public async Task Issue_ServerErrorFromTokenService_Returns400()
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1"))
                .ReturnsAsync(new ClientCredential { ClientSecret = "s1", IsActive = true });
            // Tenant not resolvable -> cert/tenant resolution fails -> server_error (not invalid_client) -> 400 branch.
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var form = new Dictionary<string, string> { ["client_id"] = "c1", ["client_secret"] = "s1" };
            var result = await Create().IssueAsync(MakeRequest(form));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        }
    }
}
