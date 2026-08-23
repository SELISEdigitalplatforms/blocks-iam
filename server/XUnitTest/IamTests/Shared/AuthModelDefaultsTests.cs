using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.ResponseModel;
using Authentication.DomainService.Shared;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Asserts the default values and property round-trips of the authentication transport models.
    /// These defaults are part of the contract (for example SendAsResponse defaulting to true), so
    /// they are worth locking down.
    /// </summary>
    public class AuthModelDefaultsTests
    {
        [Fact]
        public void TokenPayload_Defaults_AreEmptyStrings()
        {
            var payload = new TokenPayload();

            payload.Code.Should().BeEmpty();
            payload.RedirectUri.Should().BeEmpty();
            payload.Username.Should().BeEmpty();
            payload.Password.Should().BeEmpty();
            payload.Scope.Should().BeEmpty();
            payload.RefreshToken.Should().BeEmpty();
            payload.MfaId.Should().BeEmpty();
            payload.State.Should().BeEmpty();
            payload.Language.Should().BeEmpty();
            payload.BiometricId.Should().BeEmpty();
            payload.BiometricKey.Should().BeEmpty();
            payload.ClientId.Should().BeEmpty();
            payload.ClientSecret.Should().BeEmpty();
            payload.UserSecret.Should().BeEmpty();
            payload.OrganizationId.Should().BeEmpty();
            payload.RememberMe.Should().BeFalse();

            // RFC 8693 token exchange (delegated access).
            payload.SubjectToken.Should().BeEmpty();
            payload.SubjectTokenType.Should().BeEmpty();
            payload.Nonce.Should().BeEmpty();
            payload.Ts.Should().BeEmpty();
            payload.Signature.Should().BeEmpty();
        }

        [Fact]
        public void TokenPayload_RoundTrips_AllProperties()
        {
            var payload = new TokenPayload
            {
                GrantType = "password",
                Code = "code",
                RedirectUri = "https://redirect",
                Username = "user",
                Password = "pass",
                Scope = "openid",
                RememberMe = true,
                RefreshToken = "rt",
                MfaId = "mfa",
                MfaType = UserMfaType.TOTP,
                State = "state",
                Language = "en",
                BiometricId = "bid",
                BiometricKey = "bkey",
                ClientId = "cid",
                ClientSecret = "secret",
                UserSecret = "usercode",
                OrganizationId = "org",
                SubjectToken = "dg_" + new string('a', 64),
                SubjectTokenType = "urn:blocks:params:oauth:token-type:delegation-grant",
                Nonce = "0f1e2d3c4b5a69788796a5b4c3d2e1f0",
                Ts = "1739577600",
                Signature = new string('c', 64)
            };

            payload.GrantType.Should().Be("password");
            payload.Code.Should().Be("code");
            payload.RedirectUri.Should().Be("https://redirect");
            payload.Username.Should().Be("user");
            payload.Password.Should().Be("pass");
            payload.Scope.Should().Be("openid");
            payload.RememberMe.Should().BeTrue();
            payload.RefreshToken.Should().Be("rt");
            payload.MfaId.Should().Be("mfa");
            payload.MfaType.Should().Be(UserMfaType.TOTP);
            payload.State.Should().Be("state");
            payload.Language.Should().Be("en");
            payload.BiometricId.Should().Be("bid");
            payload.BiometricKey.Should().Be("bkey");
            payload.ClientId.Should().Be("cid");
            payload.ClientSecret.Should().Be("secret");
            payload.UserSecret.Should().Be("usercode");
            payload.OrganizationId.Should().Be("org");
            payload.SubjectToken.Should().Be("dg_" + new string('a', 64));
            payload.SubjectTokenType.Should().Be("urn:blocks:params:oauth:token-type:delegation-grant");
            payload.Nonce.Should().Be("0f1e2d3c4b5a69788796a5b4c3d2e1f0");
            payload.Ts.Should().Be("1739577600");
            payload.Signature.Should().Be(new string('c', 64));
        }

        [Fact]
        public void SocialLoginCredential_Defaults_AndRoundTrip()
        {
            var credential = new SocialLoginCredential
            {
                Provider = "google",
                Audience = "aud",
                ClientId = "cid",
                ClientSecret = "secret",
                AuthorizationUrl = "https://auth",
                TokenUrl = "https://token",
                GetProfileUrl = "https://profile",
                RedirectUrl = "https://redirect",
                Scope = "openid"
            };

            credential.InitialRoles.Should().BeEmpty();
            credential.InitialPermissions.Should().BeEmpty();
            credential.IsDisabled.Should().BeFalse();
            credential.SendAsResponse.Should().BeTrue();
            credential.WellKnownUrl.Should().BeNull();
            credential.GetEmailUrl.Should().BeNull();

            credential.WellKnownUrl = "https://well-known";
            credential.GetEmailUrl = "https://email";
            credential.InitialRoles.Add("admin");
            credential.InitialPermissions.Add("read");
            credential.IsDisabled = true;
            credential.SendAsResponse = false;
            credential.SSOType = SSOType.Social;
            credential.TeamId = "team";
            credential.KeyId = "key";
            credential.PrivateKey = "priv";
            credential.AppleAudience = "apple-aud";

            credential.Provider.Should().Be("google");
            credential.Audience.Should().Be("aud");
            credential.ClientId.Should().Be("cid");
            credential.ClientSecret.Should().Be("secret");
            credential.AuthorizationUrl.Should().Be("https://auth");
            credential.TokenUrl.Should().Be("https://token");
            credential.GetProfileUrl.Should().Be("https://profile");
            credential.RedirectUrl.Should().Be("https://redirect");
            credential.Scope.Should().Be("openid");
            credential.WellKnownUrl.Should().Be("https://well-known");
            credential.GetEmailUrl.Should().Be("https://email");
            credential.InitialRoles.Should().ContainSingle().Which.Should().Be("admin");
            credential.InitialPermissions.Should().ContainSingle().Which.Should().Be("read");
            credential.IsDisabled.Should().BeTrue();
            credential.SendAsResponse.Should().BeFalse();
            credential.SSOType.Should().Be(SSOType.Social);
            credential.TeamId.Should().Be("team");
            credential.KeyId.Should().Be("key");
            credential.PrivateKey.Should().Be("priv");
            credential.AppleAudience.Should().Be("apple-aud");
        }

        [Fact]
        public void GetSsoCredentialResponse_Defaults_AndRoundTrip()
        {
            var response = new GetSsoCredentialResponse
            {
                Provider = "google",
                Audience = "aud",
                ClientId = "cid",
                ClientSecret = "secret",
                AuthorizationUrl = "https://auth",
                TokenUrl = "https://token",
                GetProfileUrl = "https://profile",
                RedirectUrl = "https://redirect",
                Scope = "openid"
            };

            response.UserRoles.Should().BeEmpty();
            response.UserPermissions.Should().BeEmpty();
            response.IsDisabled.Should().BeFalse();
            response.SendAsResponse.Should().BeTrue();
            response.WellKnownUrl.Should().BeNull();

            response.WellKnownUrl = "https://well-known";
            response.UserRoles.Add(new GetUserRole());
            response.UserPermissions.Add(new GetUserPermission());
            response.IsDisabled = true;
            response.SendAsResponse = false;

            response.Provider.Should().Be("google");
            response.Audience.Should().Be("aud");
            response.ClientId.Should().Be("cid");
            response.ClientSecret.Should().Be("secret");
            response.AuthorizationUrl.Should().Be("https://auth");
            response.TokenUrl.Should().Be("https://token");
            response.GetProfileUrl.Should().Be("https://profile");
            response.RedirectUrl.Should().Be("https://redirect");
            response.Scope.Should().Be("openid");
            response.WellKnownUrl.Should().Be("https://well-known");
            response.UserRoles.Should().ContainSingle();
            response.UserPermissions.Should().ContainSingle();
            response.IsDisabled.Should().BeTrue();
            response.SendAsResponse.Should().BeFalse();
        }
    }
}
