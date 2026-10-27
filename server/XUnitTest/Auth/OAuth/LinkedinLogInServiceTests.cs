using Blocks.Secrets;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.SocialServices;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.OAuth
{
    public class LinkedinLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        private LinkedinLogInService Create() =>
            new(NullLogger<LinkedinLogInService>.Instance, _authRepo.Object, _http.Object, TestDoubles.IdentityProviderSecretTestDoubles.Resolver(_secrets));

        private static IdentityProvider Provider() => new()
        {
            Provider = "linkedin",
            ProviderType = "social",
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://linkedin/token",
            UserInfoUrl = "https://linkedin/userinfo",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State() => new()
        {
            ClientId = "client-1",
            Provider = "linkedin",
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

        private void SetupProfile(LinkedinUserInfo? info, string error = "")
        {
            _http.Setup(h => h.Get<LinkedinUserInfo>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((info!, error));
        }

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<LinkedinUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenExchangeFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "invalid_request");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<LinkedinUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenResponseNull()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "");

            var result = await Create().HandleSocialLoginCallback(State());

            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenProfileFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(null, "profile boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<LinkedinUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenProfileNullWithoutError()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(null, "");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<LinkedinUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task MapsProfile_OnSuccess()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1", IdToken = "it-1" });
            SetupProfile(new LinkedinUserInfo
            {
                Sub = "li-1",
                Given_Name = "Jane",
                Family_Name = "Roe",
                Name = "Jane Roe",
                Email = "jane@x.com",
                Picture = "https://img/jane.png"
            });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<LinkedinUserData>().Subject;
            user.ExternalProviderUserId.Should().Be("li-1");
            user.FirstName.Should().Be("Jane");
            user.LastName.Should().Be("Roe");
            user.DisplayName.Should().Be("Jane Roe");
            user.Email.Should().Be("jane@x.com");
            user.ProfileImageUrl.Should().Be("https://img/jane.png");
            user.Platform.Should().Be("linkedin");
            user.Roles.Should().ContainSingle().Which.Should().Be("init-role");
            user.Permissions.Should().ContainSingle().Which.Should().Be("perm-1");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
            result.IdToken.Should().Be("it-1");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserData()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(new LinkedinUserInfo { Sub = "li-2", Email = "e@x.com", Name = "N" });

            var user = await Create().HandleSocialLogin(State());

            user.Should().BeOfType<LinkedinUserData>();
            user.ExternalProviderUserId.Should().Be("li-2");
        }
    
        // ---------- #649: client secret resolved through Blocks Secrets ----------

        private IdentityProvider ReferencedProvider()
        {
            var idp = Provider();
            idp.ClientSecret = "stale-plaintext";
            idp.ClientSecretReference = "cs-ref";
            return idp;
        }

        [Fact]
        public async Task ReferencedProvider_SendsTheResolvedSecret_WithOneRead()
        {
            // H5, C4 (the reference wins over the legacy field), C13 (exactly one read)
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ReferencedProvider());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ReturnsAsync("vault-s");
            Dictionary<string, string>? form = null;
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken, int?>((_, f, _, _, _, _) => form = f)
                .ReturnsAsync(((SocialOauthAccessToken)null!, "stop"));

            await Create().HandleSocialLoginCallback(State());

            form.Should().NotBeNull();
            form!["client_secret"].Should().Be("vault-s");
            _secrets.Verify(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task LegacyProvider_SendsThePlaintext_WithNoSecretCall()
        {
            // H7, C13
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            Dictionary<string, string>? form = null;
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken, int?>((_, f, _, _, _, _) => form = f)
                .ReturnsAsync(((SocialOauthAccessToken)null!, "stop"));

            await Create().HandleSocialLoginCallback(State());

            form!["client_secret"].Should().Be("secret-1");
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReferencedSecretUnavailable_FailsTheSignIn_WithoutCallingTheProvider()
        {
            // C2, C3, C4 (no fallback to the legacy field)
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(ReferencedProvider());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretStateException("locked", "read"));

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<LinkedinUserData>();
            result.ExternalUserData.Email.Should().BeNullOrEmpty();
            result.AccessToken.Should().BeNull();
            _http.VerifyNoOtherCalls();
        }
    }
}
