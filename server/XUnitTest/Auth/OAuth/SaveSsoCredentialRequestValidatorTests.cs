using System.Net;
using System.Net.Http;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace XUnitTest.Auth.OAuth
{
    public class SaveSsoCredentialRequestValidatorTests
    {
        private static SaveSsoCredentialRequestValidator CreateValidator(HttpResponseMessage? response = null, Exception? throwOnSend = null)
        {
            var handler = new Mock<HttpMessageHandler>();
            var setup = handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());

            if (throwOnSend != null)
                setup.ThrowsAsync(throwOnSend);
            else
                setup.ReturnsAsync(response ?? new HttpResponseMessage(HttpStatusCode.OK));

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
            return new SaveSsoCredentialRequestValidator(factory.Object);
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        [Fact]
        public async Task NonByosso_Type_SkipsUrlRule()
        {
            // For SSOType.Social the WellKnownUrl rule is not evaluated at all.
            var validator = CreateValidator();
            var request = new SaveSsoCredentialRequest { SSOType = SSOType.Social, WellKnownUrl = null };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Byosso_InvalidUrl_Fails()
        {
            var validator = CreateValidator();
            var request = new SaveSsoCredentialRequest { SSOType = SSOType.BYOSSO, WellKnownUrl = "not-a-valid-url" };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Byosso_ValidUrl_MetadataNotFound_Fails()
        {
            var validator = CreateValidator(new HttpResponseMessage(HttpStatusCode.NotFound));
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration"
            };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage == "WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Byosso_ValidUrl_MetadataMissingTokenEndpoint_Fails()
        {
            var validator = CreateValidator(JsonResponse(HttpStatusCode.OK,
                "{\"authorization_endpoint\":\"https://idp/auth\"}"));
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration"
            };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage == "WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Byosso_ValidUrl_ValidMetadata_Passes()
        {
            var validator = CreateValidator(JsonResponse(HttpStatusCode.OK,
                "{\"authorization_endpoint\":\"https://idp/auth\",\"token_endpoint\":\"https://idp/token\"}"));
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration"
            };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Byosso_ValidUrl_HttpThrows_Fails()
        {
            var validator = CreateValidator(throwOnSend: new HttpRequestException("network down"));
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration"
            };

            var result = await validator.ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage == "WellKnownUrl does not expose valid OpenID Connect metadata");
        }
    }
}
