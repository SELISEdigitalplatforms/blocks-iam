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

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// Covers Microsoft profile resolution for personal (MSA) accounts: the consumer-safe Graph
    /// retry and the id_token claim fallback that keeps login working when Graph rejects the
    /// request outright. Organizational-account behaviour must stay byte-for-byte identical.
    /// </summary>
    public class MicrosoftProfileEnrichmentTests
    {
        private const string ClientId = "client-1";
        private const string TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        private const string GraphUrl = "https://graph.microsoft.com/v1.0/me";
        private const string MicrosoftIssuer = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0";

        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();
        private readonly List<string> _graphCalls = [];

        private MicrosoftLogInService CreateMicrosoft() =>
            new(NullLogger<MicrosoftLogInService>.Instance, _authRepo.Object, _http.Object);

        private static IdentityProvider Provider() => new()
        {
            Provider = "microsoft",
            ProviderType = "social",
            ClientId = ClientId,
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = TokenUrl,
            UserInfoUrl = GraphUrl,
            InitialRoles = ["init-role"],
            InitialPermissions = ["perm-1"]
        };

        private static StateInfo State() => new()
        {
            ClientId = ClientId,
            Provider = "microsoft",
            Audience = "aud-1",
            Code = "auth-code",
            RedirectUri = "https://app/callback"
        };

        private void SetupProviderAndToken(string idToken)
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(
                    It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((new SocialOauthAccessToken { AccessToken = "at-1", IdToken = idToken }, ""));
        }

        /// <summary>Answers the Graph call based on whether the projection asks for directory-only attributes.</summary>
        private void SetupGraph(MicrosoftUserData? directoryResult, string directoryError, MicrosoftUserData? consumerResult, string consumerError)
        {
            _http.Setup(h => h.Get<MicrosoftUserData>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((string url, Dictionary<string, string> _, CancellationToken _, int? _) =>
                {
                    _graphCalls.Add(url);
                    return url.Contains("department", StringComparison.Ordinal)
                        ? (directoryResult!, directoryError)
                        : (consumerResult!, consumerError);
                });
        }

        private const string Graph403 =
            "{\"error\":{\"code\":\"UnknownError\",\"message\":\"\",\"innerError\":{\"date\":\"2026-08-16T10:47:19\"}}}";

        // ---------- FIX A: consumer-safe Graph projection (H1, H7) ----------

        [Fact]
        public async Task Callback_RetriesWithoutDirectoryAttributes_WhenGraphRejectsProjection()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "user@outlook.com")));
            SetupGraph(null, Graph403, new MicrosoftUserData { UserPrincipalName = "user@outlook.com", ExternalProviderUserId = "msa-1" }, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            _graphCalls.Should().HaveCount(2);
            _graphCalls[0].Should().Contain("department").And.Contain("employeeId");
            _graphCalls[1].Should().NotContain("department").And.NotContain("employeeId");
            result.ExternalUserData.UserPrincipalName.Should().Be("user@outlook.com");
            result.ExternalUserData.ExternalProviderUserId.Should().Be("msa-1");
        }

        [Fact]
        public async Task Callback_CompletesWithEmptyDirectoryAttributes_WhenRetryPathUsed()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "user@outlook.com")));
            SetupGraph(null, Graph403, new MicrosoftUserData { UserPrincipalName = "user@outlook.com", ExternalProviderUserId = "msa-1" }, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Department.Should().BeNullOrEmpty();
            result.ExternalUserData.EmployeeId.Should().BeNullOrEmpty();
            result.ExternalUserData.UserPrincipalName.Should().Be("user@outlook.com");
        }

        // ---------- Organizational accounts unchanged (H4) ----------

        [Fact]
        public async Task Callback_OrgAccount_MakesSingleGraphCall_AndKeepsDirectoryAttributes()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "other@outlook.com")));
            SetupGraph(
                new MicrosoftUserData
                {
                    Email = "user@company.com",
                    UserPrincipalName = "user@company.com",
                    ExternalProviderUserId = "aad-1",
                    Department = "Engineering",
                    EmployeeId = "E-42"
                },
                "", null, "should not be reached");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            _graphCalls.Should().ContainSingle();
            result.ExternalUserData.Email.Should().Be("user@company.com");
            result.ExternalUserData.Department.Should().Be("Engineering");
            result.ExternalUserData.EmployeeId.Should().Be("E-42");
            result.ExternalUserData.ExternalProviderUserId.Should().Be("aad-1");
        }

        [Fact]
        public async Task Callback_OrgAccount_GraphEmailWins_OverIdTokenClaim()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "claim@outlook.com"), ("oid", "claim-oid")));
            SetupGraph(new MicrosoftUserData { Email = "user@company.com", ExternalProviderUserId = "aad-1" }, "", null, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("user@company.com");
            result.ExternalUserData.ExternalProviderUserId.Should().Be("aad-1");
        }

        // ---------- FIX B: id_token fallback (H2, H3, H6) ----------

        /// <summary>Regression test for the reported bug: Graph 403 on both projections, identity from id_token.</summary>
        [Fact]
        public async Task Callback_ResolvesEmailAndUserIdFromIdToken_WhenGraphFailsEntirely()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId,
                ("preferred_username", "user@outlook.com"), ("oid", "msa-oid"), ("name", "MSA User"),
                ("given_name", "MSA"), ("family_name", "User")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("user@outlook.com");
            result.ExternalUserData.ExternalProviderUserId.Should().Be("msa-oid");
            result.ExternalUserData.DisplayName.Should().Be("MSA User");
            result.ExternalUserData.FirstName.Should().Be("MSA");
            result.ExternalUserData.LastName.Should().Be("User");
        }

        [Fact]
        public async Task Callback_FallsBackToSubClaim_WhenOidAbsent()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "user@outlook.com"), ("sub", "msa-sub")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.ExternalProviderUserId.Should().Be("msa-sub");
        }

        [Fact]
        public async Task Callback_ResolvesEmailFromEmailClaim_WhenGraphReturnsNullEmailFields()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("email", "live.user@hotmail.com")));
            SetupGraph(new MicrosoftUserData { Email = null!, UserPrincipalName = null!, ExternalProviderUserId = "msa-1" }, "", null, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            _graphCalls.Should().ContainSingle();
            result.ExternalUserData.Email.Should().Be("live.user@hotmail.com");
            result.ExternalUserData.ExternalProviderUserId.Should().Be("msa-1");
        }

        [Fact]
        public async Task Callback_ResolvesEmailFromUpnClaim_WhenPreferredUsernameAndEmailAbsent()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("upn", "upn.user@company.com")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("upn.user@company.com");
        }

        [Fact]
        public async Task Callback_DoesNotOverrideGraphUserPrincipalName_WithIdTokenClaim()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "claim@outlook.com")));
            SetupGraph(new MicrosoftUserData { Email = null!, UserPrincipalName = "graph@company.com", ExternalProviderUserId = "aad-1" }, "", null, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            // UserPrincipalName is a valid email source; NormalizeExternalUserEmail promotes it later.
            result.ExternalUserData.Email.Should().BeNull();
            result.ExternalUserData.UserPrincipalName.Should().Be("graph@company.com");
        }

        // ---------- C1: malformed / absent id_token must not throw ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-jwt")]
        [InlineData("aaa.bbb.ccc")]
        public async Task Callback_DoesNotThrow_WhenIdTokenUnusable(string? idToken)
        {
            SetupProviderAndToken(idToken!);
            SetupGraph(null, Graph403, null, Graph403);

            var act = async () => await CreateMicrosoft().HandleSocialLoginCallback(State());

            var result = await act.Should().NotThrowAsync();
            result.Subject.ExternalUserData.Email.Should().BeNullOrEmpty();
            result.Subject.ExternalUserData.Roles.Should().BeEquivalentTo(new[] { "init-role" });
        }

        [Fact]
        public async Task Callback_DoesNotThrow_WhenIdTokenUnusable_AndGraphSucceeds()
        {
            SetupProviderAndToken("not-a-jwt");
            SetupGraph(new MicrosoftUserData { Email = "user@company.com", ExternalProviderUserId = "aad-1" }, "", null, "");

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("user@company.com");
        }

        // ---------- C2: aud / iss must match before a claim establishes identity ----------

        [Fact]
        public async Task Callback_IgnoresIdTokenClaims_WhenAudienceDoesNotMatchClientId()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, "some-other-client", ("preferred_username", "attacker@evil.com"), ("oid", "x")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().BeNullOrEmpty();
            result.ExternalUserData.ExternalProviderUserId.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task Callback_IgnoresIdTokenClaims_WhenIssuerHostDoesNotMatchTokenEndpoint()
        {
            SetupProviderAndToken(CreateJwt("https://evil.example.com/v2.0", ClientId, ("preferred_username", "attacker@evil.com"), ("oid", "x")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().BeNullOrEmpty();
            result.ExternalUserData.ExternalProviderUserId.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task Callback_AcceptsIdTokenClaims_WhenIssuerHostMatchesTokenEndpoint()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId, ("preferred_username", "user@outlook.com"), ("oid", "msa-oid")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("user@outlook.com");
        }

        // ---------- roles keep working across both branches ----------

        [Fact]
        public async Task Callback_StillExtractsRoles_WhenGraphFails()
        {
            SetupProviderAndToken(CreateJwt(MicrosoftIssuer, ClientId,
                ("preferred_username", "user@outlook.com"), ("roles", "[\"ms-role\"]")));
            SetupGraph(null, Graph403, null, Graph403);

            var result = await CreateMicrosoft().HandleSocialLoginCallback(State());

            result.ExternalUserData.Roles.Should().BeEquivalentTo(new[] { "ms-role", "init-role" });
        }

        private static string CreateJwt(string issuer, string audience, params (string Key, string Value)[] claims)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                "test-key-with-sufficient-length-for-hmacsha256-algorithm"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwtClaims = claims.Select(c => new Claim(c.Key, c.Value)).ToList();
            var token = new JwtSecurityToken(
                issuer: issuer, audience: audience, claims: jwtClaims,
                expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
            return handler.WriteToken(token);
        }
    }
}
