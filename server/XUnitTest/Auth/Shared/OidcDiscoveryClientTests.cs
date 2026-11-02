using System.Net;
using System.Text;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    public class OidcDiscoveryClientTests
    {
        [Fact]
        public void HttpClientName_IsOidcDiscovery()
        {
            OidcDiscoveryClient.HttpClientName.Should().Be("oidc-discovery");
        }

        [Fact]
        public async Task GetMetadataAsync_CallsHttpClient()
        {
            var factory = new Mock<IHttpClientFactory>();
            var httpClient = new HttpClient(new FakeHandler())
            {
                BaseAddress = new Uri("https://test.com/")
            };
            factory.Setup(f => f.CreateClient(OidcDiscoveryClient.HttpClientName)).Returns(httpClient);

            var client = new OidcDiscoveryClient(factory.Object);
            var result = await client.GetMetadataAsync("https://test.com/.well-known/openid-configuration");

            result.Should().NotBeNull();
        }

        [Theory]
        // Rollbar #1809/#1810: a web app answers an unknown path with its HTML page and a 200.
        [InlineData(HttpStatusCode.OK, "<!doctype html><html></html>", "text/html")]
        [InlineData(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", "text/html")]
        [InlineData(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}", "application/json")]
        [InlineData(HttpStatusCode.OK, "", "text/plain")]
        public async Task GetMetadataAsync_NotDiscoveryJson_IsA400NamingTheUrl(HttpStatusCode status, string body, string mediaType)
        {
            var client = new OidcDiscoveryClient(Factory(new FixedHandler(status, body, mediaType)));

            var act = () => client.GetMetadataAsync("https://idp.example.com/.well-known/openid-configuration");

            var error = (await act.Should().ThrowAsync<BlocksValidationException>()).Which;
            error.Errors["WellKnownUrl"].Should().ContainSingle()
                .Which.Should().Contain("https://idp.example.com/.well-known/openid-configuration")
                .And.Contain($"HTTP {(int)status}").And.Contain(mediaType);
        }

        private static IHttpClientFactory Factory(HttpMessageHandler handler)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(OidcDiscoveryClient.HttpClientName)).Returns(new HttpClient(handler));
            return factory.Object;
        }

        private sealed class FixedHandler(HttpStatusCode status, string body, string mediaType) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }

        private class FakeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var json = @"{
                    ""issuer"": ""https://test.com/"",
                    ""authorization_endpoint"": ""https://test.com/auth"",
                    ""token_endpoint"": ""https://test.com/token""
                }";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }
        }
    }
}