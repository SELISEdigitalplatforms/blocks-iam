using Authentication.DomainService.Shared.Services;
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