using Blocks.Secrets;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// Exercises the shared logic of <see cref="SocialLogInServiceBase"/>
    /// (HandleSocialLoginCallback / HandleSocialLogin and the Google/Microsoft
    /// profile-verification helpers) through its concrete subclasses. The
    /// existing SocialLogInServiceBaseTests only covers ExtractRolesFromJwt.
    /// </summary>
    public class SocialLogInServiceBaseExtraTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        private GoogleLogInService CreateGoogle() =>
            new(NullLogger<GoogleLogInService>.Instance, _authRepo.Object, _http.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets));

        private MicrosoftLogInService CreateMicrosoft() =>
            new(NullLogger<MicrosoftLogInService>.Instance, _authRepo.Object, _http.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets));

        private static IdentityProvider Provider() => new()
        {
            Provider = "google",
            ProviderType = "social",
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://idp/token",
            UserInfoUrl = "https://idp/userinfo?token={0}",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State(string provider) => new()
        {
            ClientId = "client-1",
            Provider = provider,
            Audience = "aud-1",
            Code = "auth-code",
            RedirectUri = "https://app/callback"
        };

        private void SetupToken(SocialOauthAccessToken? token, string error = "")
        {
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(
                    It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((token!, error));
        }

        private void SetupGoogleUser(GoogleUserData? user, string error = "")
        {
            _http.Setup(h => h.Get<GoogleUserData>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((user!, error));
        }

        private void SetupMicrosoftUser(MicrosoftUserData? user, string error = "")
        {
            _http.Setup(h => h.Get<MicrosoftUserData>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((user!, error));
        }

        // ---------- identity provider lookup ----------

        [Fact]
        public async Task Callback_ReturnsEmptyUserData_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await CreateGoogle().HandleSocialLoginCallback(State("google"));

            result.ExternalUserData.Should().BeOfType<GoogleUserData>();
            result.ExternalUserData.Email.Should().BeNull();
            result.AccessToken.Should().BeNull();
            _http.Verify(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()),
                Times.Never);
        }

        // ---------- token exchange failure ----------

        [Fact]
        public async Task Callback_ReturnsEmptyUserData_WhenTokenExchangeReturnsError()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "invalid_grant");

            var result = await CreateGoogle().HandleSocialLoginCallback(State("google"));

            result.ExternalUserData.Email.Should().BeNull();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task Callback_ReturnsEmptyUserData_WhenTokenResponseIsNull()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "");

            var result = await CreateGoogle().HandleSocialLoginCallback(State("google"));

            result.AccessToken.Should().BeNull();
        }

        // ---------- Google happy path ----------

        [Fact]
        public async Task Callback_Google_MapsUserAndAssignsInitialRoles_WhenNoRolesFromProvider()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1", IdToken = "it-1" });
            SetupGoogleUser(new GoogleUserData { Email = "g@user.com", ExternalProviderUserId = "gid-1", DisplayName = "G User" });

            var result = await CreateGoogle().HandleSocialLoginCallback(State("google"));

            result.ExternalUserData.Should().BeOfType<GoogleUserData>();
            result.ExternalUserData.Email.Should().Be("g@user.com");
            result.ExternalUserData.Platform.Should().Be("google");
            result.ExternalUserData.Permissions.Should().ContainSingle().Which.Should().Be("perm-1");
            // no roles from provider -> replaced with initial roles
            result.ExternalUserData.Roles.Should().ContainSingle().Which.Should().Be("init-role");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
            result.IdToken.Should().Be("it-1");
        }

        [Fact]
        public async Task Callback_Google_ReturnsEmptyUserData_ButKeepsToken_WhenProfileFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupGoogleUser(null, "profile boom");

            var result = await CreateGoogle().HandleSocialLoginCallback(State("google"));

            result.ExternalUserData.Should().BeOfType<GoogleUserData>();
            result.ExternalUserData.Email.Should().BeNull();
            // base still populates token and provider metadata after empty profile
            result.AccessToken.Should().Be("at-1");
            result.ExternalUserData.Platform.Should().Be("google");
        }

        // ---------- Microsoft happy path (roles from id_token) ----------

        [Fact]
        public async Task Callback_Microsoft_MapsUserAndMergesRolesFromIdToken()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            var idToken = CreateJwt(("roles", "[\"ms-role\"]"));
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-2", IdToken = idToken });
            SetupMicrosoftUser(new MicrosoftUserData { Email = "m@user.com", ExternalProviderUserId = "mid-1", DisplayName = "M User" });

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State("microsoft"));

            result.ExternalUserData.Should().BeOfType<MicrosoftUserData>();
            result.ExternalUserData.Email.Should().Be("m@user.com");
            result.ExternalUserData.Platform.Should().Be("microsoft");
            // roles present from JWT -> initial roles appended
            result.ExternalUserData.Roles.Should().BeEquivalentTo(new[] { "ms-role", "init-role" });
            result.AccessToken.Should().Be("at-2");
        }

        [Fact]
        public async Task Callback_Microsoft_ReturnsEmptyUserData_WhenProfileFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-2", IdToken = CreateJwt(("sub", "s")) });
            SetupMicrosoftUser(null, "graph boom");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State("microsoft"));

            result.ExternalUserData.Should().BeOfType<MicrosoftUserData>();
            result.ExternalUserData.Email.Should().BeNull();
            result.AccessToken.Should().Be("at-2");
        }

        // ---------- default (unknown provider) branch ----------

        [Fact]
        public async Task Callback_UnknownProvider_ReturnsEmptyUserData_WithToken()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-3" });

            // Provider is neither "google" nor "microsoft" -> switch default -> CreateEmptyUserData
            var result = await CreateGoogle().HandleSocialLoginCallback(State("custom-provider"));

            result.ExternalUserData.Should().BeOfType<GoogleUserData>();
            result.ExternalUserData.Email.Should().BeNull();
            result.ExternalUserData.Platform.Should().Be("custom-provider");
            result.AccessToken.Should().Be("at-3");
        }

        // ---------- HandleSocialLogin delegates to callback ----------

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserDataFromCallback()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-4" });
            SetupGoogleUser(new GoogleUserData { Email = "d@user.com", ExternalProviderUserId = "did" });

            var user = await CreateGoogle().HandleSocialLogin(State("google"));

            user.Should().BeOfType<GoogleUserData>();
            user.Email.Should().Be("d@user.com");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsEmptyUserData_WhenProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var user = await CreateGoogle().HandleSocialLogin(State("google"));

            user.Should().BeOfType<GoogleUserData>();
            user.Email.Should().BeNull();
        }

        private static string CreateJwt(params (string Key, string Value)[] claims)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                "test-key-with-sufficient-length-for-hmacsha256-algorithm"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwtClaims = claims.Select(c => new Claim(c.Key, c.Value)).ToList();
            var token = new JwtSecurityToken(
                issuer: "test-issuer", audience: "test-audience", claims: jwtClaims,
                expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
            return handler.WriteToken(token);
        }
    
        // ---------- #649: client secret resolved through Blocks Secrets ----------

        private IdentityProvider ReferencedProvider()
        {
            var idp = Provider();
            idp.ClientSecret = "stale-plaintext";
            idp.ClientSecretReference = "cs-ref";
            return idp;
        }

        [Theory]
        [InlineData("google")]
        [InlineData("microsoft")]
        public async Task ReferencedProvider_SendsTheResolvedSecret_WithOneRead(string provider)
        {
            // H5, C4 (the reference wins over the legacy field), C13 (exactly one read)
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ReferencedProvider());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ReturnsAsync("vault-s");
            Dictionary<string, string>? form = null;
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken, int?>((_, f, _, _, _, _) => form = f)
                .ReturnsAsync(((SocialOauthAccessToken)null!, "stop"));

            await (provider == "google" ? (SocialLogInServiceBase)CreateGoogle() : CreateMicrosoft()).HandleSocialLoginCallback(State(provider));

            form.Should().NotBeNull();
            form!["client_secret"].Should().Be("vault-s");
            _secrets.Verify(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("google")]
        [InlineData("microsoft")]
        public async Task LegacyProvider_SendsThePlaintext_WithNoSecretCall(string provider)
        {
            // H7, C13
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            Dictionary<string, string>? form = null;
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken, int?>((_, f, _, _, _, _) => form = f)
                .ReturnsAsync(((SocialOauthAccessToken)null!, "stop"));

            await (provider == "google" ? (SocialLogInServiceBase)CreateGoogle() : CreateMicrosoft()).HandleSocialLoginCallback(State(provider));

            form!["client_secret"].Should().Be("secret-1");
            _secrets.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("google")]
        [InlineData("microsoft")]
        public async Task ReferencedSecretUnavailable_FailsTheSignIn_WithoutCallingTheProvider(string provider)
        {
            // C2, C3, C4 (no fallback to the legacy field)
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ReferencedProvider());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretStateException("locked", "read"));

            var result = await (provider == "google" ? (SocialLogInServiceBase)CreateGoogle() : CreateMicrosoft()).HandleSocialLoginCallback(State(provider));

            result.ExternalUserData.Should().BeOfType(provider == "google" ? typeof(GoogleUserData) : typeof(MicrosoftUserData));
            result.ExternalUserData.Email.Should().BeNullOrEmpty();
            result.AccessToken.Should().BeNull();
            _http.VerifyNoOtherCalls();
        }
    }
}
