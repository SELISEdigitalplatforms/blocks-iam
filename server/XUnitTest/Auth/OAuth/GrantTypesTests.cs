using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class GrantTypesTests
    {
        [Theory]
        [InlineData(GrantTypes.RefreshToken, "refresh_token")]
        [InlineData(GrantTypes.Password, "password")]
        [InlineData(GrantTypes.MfaCode, "mfa_code")]
        [InlineData(GrantTypes.Social, "social")]
        [InlineData(GrantTypes.AuthCode, "authorization_code")]
        [InlineData(GrantTypes.BiometricAuthorization, "biometric_authorization")]
        [InlineData(GrantTypes.ClientCredential, "client_credentials")]
        [InlineData(GrantTypes.ClientUserCode, "client_user_code")]
        [InlineData(GrantTypes.SwitchOrganization, "switch_organization")]
        [InlineData(GrantTypes.SsoConsentCode, "sso_consent")]
        [InlineData(GrantTypes.ImpersonationCloud, "impersonation_cloud")]
        public void GrantTypes_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }
    }
}