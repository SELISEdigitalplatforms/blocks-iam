using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace XUnitTest.Auth.OAuth
{
    public class OAuthErrorTests
    {
        [Fact]
        public void InvalidRequest_ReturnsBadRequest_WithDefaults()
        {
            var result = OAuthError.InvalidRequest();

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new
            {
                error = "invalid_request",
                error_description = "The request is missing a required parameter.",
                state = ""
            });
        }

        [Fact]
        public void InvalidRequest_PropagatesDescriptionAndState()
        {
            var result = OAuthError.InvalidRequest("missing code", "st-1");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new
            {
                error = "invalid_request",
                error_description = "missing code",
                state = "st-1"
            });
        }

        [Fact]
        public void UnsupportedGrantType_ReturnsBadRequest_WithState()
        {
            var result = OAuthError.UnsupportedGrantType("st-2");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new
            {
                error = "unsupported_grant_type",
                error_description = "The grant type is not supported.",
                state = "st-2"
            });
        }

        [Fact]
        public void UnauthorizedClient_ReturnsBadRequest()
        {
            var result = OAuthError.UnauthorizedClient("st-3");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new
            {
                error = "unauthorized_client",
                error_description = "The client is not authorized to request an authorization code using this method.",
                state = "st-3"
            });
        }

        [Fact]
        public void Error400Response_WrapsErrorAndDescription()
        {
            var result = OAuthError.Error400Response("bad", "the reason");

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "bad", error_description = "the reason" });
        }

        [Fact]
        public void Error401Response_ReturnsUnauthorized()
        {
            var result = OAuthError.Error401Response("nope", "no auth");

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauth.Value.Should().BeEquivalentTo(new { error = "nope", error_description = "no auth" });
        }

        [Theory]
        [InlineData(GrantTypes.Password, OAuthError.InValidUseNamePassword, 401)]
        [InlineData(GrantTypes.MfaCode, OAuthError.InvalidRequestBody, 400)]
        [InlineData(GrantTypes.AuthCode, OAuthError.InvalidRequestBody, 400)]
        [InlineData(GrantTypes.SsoConsentCode, OAuthError.InvalidRequestBody, 400)]
        [InlineData(GrantTypes.ImpersonationCloud, OAuthError.InvalidImpersonationRequest, 400)]
        [InlineData("something_else", OAuthError.InvalidGrantType, 400)]
        [InlineData(null, OAuthError.InvalidGrantType, 400)]
        public void InValidResponse_MapsGrantTypeToError(string? grantType, string expectedError, int expectedStatus)
        {
            var response = OAuthError.InValidResponse(new TokenRequest { GrantType = grantType });

            response.Error.Should().Be(expectedError);
            response.StatusCode.Should().Be(expectedStatus);
            response.ErrorDescription.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void UserNotActiveOrVerifiedResponse_HasExpectedShape()
        {
            var response = OAuthError.UserNotActiveOrVerifiedResponse();

            response.Error.Should().Be(OAuthError.UserInActiveOrNotVerified);
            response.StatusCode.Should().Be(400);
            response.ErrorDescription.Should().Be("User is not active or verified");
        }

        [Fact]
        public void InValidOrganization_IncludesOrganizationName()
        {
            var response = OAuthError.InValidOrganization("acme");

            response.Error.Should().Be(OAuthError.UserInActiveOrNotVerified);
            response.StatusCode.Should().Be(400);
            response.ErrorDescription.Should().Contain("acme");
        }
    }
}
