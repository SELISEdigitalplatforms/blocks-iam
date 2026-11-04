using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="DiscoveryController"/> (OIDC discovery / JWKS endpoints). The
    /// discovery and jwks services are mocked; each test asserts the success result or the
    /// 500 error branch on exception.
    /// </summary>
    public class DiscoveryControllerTests : IDisposable
    {
        private readonly Mock<IDiscoveryService> _discovery = new();
        private readonly Mock<IJwksService> _jwks = new();

        public DiscoveryControllerTests()
        {
            BlocksContext.IsTestMode = true;
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private DiscoveryController CreateController()
        {
            var controller = new DiscoveryController(_discovery.Object, _jwks.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return controller;
        }

        // ---------- OpenIdConfiguration ----------

        [Fact]
        public async Task OpenIdConfiguration_Success_ReturnsOk()
        {
            _discovery.Setup(d => d.GetMetadataAsync()).ReturnsAsync(new DiscoveryMetadata());

            var result = await CreateController().OpenIdConfiguration();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task OpenIdConfiguration_ServiceThrows_Returns500()
        {
            _discovery.Setup(d => d.GetMetadataAsync()).ThrowsAsync(new Exception("boom"));

            var result = await CreateController().OpenIdConfiguration();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
        }

        // ---------- OAuthAuthorizationServer ----------

        [Fact]
        public async Task OAuthAuthorizationServer_Success_ReturnsOk()
        {
            _discovery.Setup(d => d.GetAuthorizationServerMetadataAsync()).ReturnsAsync(new OAuthAuthorizationServerMetadata());

            var result = await CreateController().OAuthAuthorizationServer();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task OAuthAuthorizationServer_ServiceThrows_Returns500()
        {
            _discovery.Setup(d => d.GetAuthorizationServerMetadataAsync()).ThrowsAsync(new Exception("boom"));

            var result = await CreateController().OAuthAuthorizationServer();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
        }

        // ---------- JwksJson ----------

        [Fact]
        public async Task JwksJson_Success_ReturnsOk()
        {
            _jwks.Setup(j => j.GetKeysAsync()).ReturnsAsync(new JwksResponse());

            var result = await CreateController().JwksJson();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task JwksJson_ServiceThrows_Returns500()
        {
            _jwks.Setup(j => j.GetKeysAsync()).ThrowsAsync(new Exception("boom"));

            var result = await CreateController().JwksJson();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task JwksJsonAlias_DelegatesToJwksJson_ReturnsOk()
        {
            _jwks.Setup(j => j.GetKeysAsync()).ReturnsAsync(new JwksResponse());

            var result = await CreateController().JwksJsonAlias();

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
