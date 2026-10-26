using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth
{
    public class AuthorizationCodeExchangeServiceTests : IDisposable
    {
        private readonly Mock<IAuthorizationCodeRepository> _authCodeRepo = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo = new();
        private readonly Mock<ITokenGenerationService> _tokenService = new();
        private readonly Mock<IPkceService> _pkce = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ITokenRevocationRepository> _tokenRevocation = new();
        private readonly Mock<ICacheClient> _cache = new();

        public AuthorizationCodeExchangeServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            _claimsResolver.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = new() { "admin" }, Permissions = new() { "read" } });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("id-token");
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("access-token");
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Consumed);
            _authCodeRepo.Setup(r => r.RecordMintAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<AuthorizationCodeMint>())).ReturnsAsync(true);
        }

        [Fact]
        public async Task Exchange_CodeAlreadyUsed_ReturnsInvalidGrantAndMintsNothing()
        {
            SetupValidExchange();
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.Is<AuthorizationCodeModel>(c => c.Code == "auth-code"))).ReturnsAsync(AuthorizationCodeConsumeResult.Refused);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            _tokenService.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        // ---------- SPEC2: single use, reuse revoke, mint record ----------

        private const string Jti = "jti-1";

        private static string SignedAccessToken()
        {
            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                claims: new[] { new System.Security.Claims.Claim("jti", Jti), new System.Security.Claims.Claim("sub", "user-1") },
                expires: DateTime.UtcNow.AddMinutes(5));
            return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
        }

        private void MintRealAccessToken() =>
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(SignedAccessToken());

        [Fact]
        public async Task Exchange_FirstUse_RecordsMintWithLineageJtiAndUser_BeforeAnswer()
        {
            SetupValidExchange();
            MintRealAccessToken();
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", RefreshTokenSessionId = "lineage-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            AuthorizationCodeMint? recorded = null;
            _authCodeRepo.Setup(r => r.RecordMintAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<AuthorizationCodeMint>()))
                .Callback<AuthorizationCodeModel, AuthorizationCodeMint>((_, m) => recorded = m)
                .ReturnsAsync(true);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            result.Should().BeOfType<OkObjectResult>();
            recorded.Should().NotBeNull();
            recorded!.LineageId.Should().Be("lineage-1");
            recorded.Jti.Should().Be(Jti);
            recorded.UserId.Should().Be("user-1");
            recorded.AccessExpiresAt.Should().BeAfter(DateTime.UtcNow);
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_PassesTheBrowserThatSignedIn_ToTheRefreshToken()
        {
            // A confidential client redeems the code from its server; the refresh token must record the
            // browser the code was issued to, as stored on the code.
            const string chromeOnWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/155.0.0.0";
            SetupValidExchange();
            MintRealAccessToken();
            var code = ValidAuthCode();
            code.CreatedByIpAddress = "203.0.113.10";
            code.CreatedByUserAgent = chromeOnWindows;
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(code);
            OidcClaims? passed = null;
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .Callback<OidcClaims, string, bool, string?>((claims, _, _, _) => passed = claims)
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            result.Should().BeOfType<OkObjectResult>();
            passed.Should().NotBeNull();
            passed!.SignInIpAddress.Should().Be("203.0.113.10");
            passed.SignInUserAgent.Should().Be(chromeOnWindows);
        }

        [Fact]
        public async Task Exchange_AccessTokenClaims_CarryTheLoadedUsersTokenVersionAndSecurityStamp()
        {
            SetupValidExchange();
            var user = ValidUser();
            user.TokenVersion = 4;
            user.SecurityStamp = "S2";
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(user);
            OidcClaims? passed = null;
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((claims, _, _) => passed = claims)
                .ReturnsAsync(SignedAccessToken());

            var result = await Create().ExchangeAsync(BuildContext().Request);

            result.Should().BeOfType<OkObjectResult>();
            passed!.Sub.Should().Be("user-1");
            passed.TokenVersion.Should().Be(4);
            passed.SecurityStamp.Should().Be("S2");
            // No extra user read: the user loaded to check the code is the one used.
            _userRepo.Verify(r => r.GetUserByIdAsync("user-1"), Times.Once);
        }

        [Fact]
        public async Task Exchange_ReadAndConsume_UseTheSameModel_SoOneStorePerRequest()
        {
            SetupValidExchange();
            var mongoCode = ValidAuthCode();
            mongoCode.Source = AuthorizationCodeSource.Mongo;
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(mongoCode);

            await Create().ExchangeAsync(BuildContext().Request);

            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.Is<AuthorizationCodeModel>(c => ReferenceEquals(c, mongoCode))), Times.Once);
            _authCodeRepo.Verify(r => r.RecordMintAsync(It.Is<AuthorizationCodeModel>(c => ReferenceEquals(c, mongoCode)), It.IsAny<AuthorizationCodeMint>()), Times.Once);
        }

        [Fact]
        public async Task Exchange_Reuse_RevokesOnlyThatCodesLineageAndJti_ReturnsTodaysInvalidGrant()
        {
            SetupValidExchange();
            var expires = DateTime.UtcNow.AddMinutes(4);
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Reused);
            _authCodeRepo.Setup(r => r.MarkReusedAsync(It.IsAny<AuthorizationCodeModel>()))
                .ReturnsAsync(new AuthorizationCodeMint { LineageId = "lineage-1", Jti = Jti, UserId = "user-1", AccessExpiresAt = expires });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            Prop(bad.Value, "error_description").Should().Be("Authorization code is invalid or expired");
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync("lineage-1", "authorization_code_reuse"), Times.Once);
            _tokenRevocation.Verify(r => r.RevokeTokenAsync(Jti, "user-1", "authorization_code_reuse", expires), Times.Once);
            _tokenService.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
            // Other sessions of the user / IdP session are not touched.
            _refreshTokenRepo.Verify(r => r.RevokeAllBySessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _refreshTokenRepo.Verify(r => r.RevokeSupersededLoginLineagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_Reuse_BeforeAnythingWasMinted_RevokesNothing()
        {
            SetupValidExchange();
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Reused);
            _authCodeRepo.Setup(r => r.MarkReusedAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync((AuthorizationCodeMint?)null);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_grant");
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _tokenRevocation.Verify(r => r.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_RefusedConsume_MongoCodeOrKeyGone_OnlyInvalidGrant_NoReuseRule()
        {
            SetupValidExchange();
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Refused);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            Prop(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_grant");
            _authCodeRepo.Verify(r => r.MarkReusedAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_ReuseWonTheRaceAgainstTheMint_RevokesAndEvictsTheNewTokens_SetsNoCookie()
        {
            SetupValidExchange();
            MintRealAccessToken();
            _tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", RefreshTokenSessionId = "lineage-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            _authCodeRepo.Setup(r => r.RecordMintAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<AuthorizationCodeMint>())).ReturnsAsync(false);
            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = "",
                Applications = new List<Applications>
                {
                    new() { Domain = "app.example.com", CookieDomain = ".example.com", IsDomainVerified = true }
                },
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
            };
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            var ctx = BuildContext(origin: "https://app.example.com");

            var result = await Create().ExchangeAsync(ctx.Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            ctx.Response.Headers.Should().NotContainKey("Set-Cookie");
            _cache.Verify(c => c.RemoveKeyAsync("rt-1"), Times.Once);
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync("lineage-1", "authorization_code_reuse"), Times.Once);
            _tokenRevocation.Verify(r => r.RevokeTokenAsync(Jti, "user-1", "authorization_code_reuse", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Exchange_RevokeFails_FailsTheRequest_NoRetry()
        {
            SetupValidExchange();
            _authCodeRepo.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Reused);
            _authCodeRepo.Setup(r => r.MarkReusedAsync(It.IsAny<AuthorizationCodeModel>()))
                .ReturnsAsync(new AuthorizationCodeMint { LineageId = "lineage-1", Jti = Jti, UserId = "user-1" });
            _refreshTokenRepo.Setup(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("store down"));

            var act = () => Create().ExchangeAsync(BuildContext().Request);

            await act.Should().ThrowAsync<TimeoutException>();
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Exchange_StoreError_FailsTheRequest_NoTokens()
        {
            SetupValidExchange();
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ThrowsAsync(new TimeoutException("redis down"));

            var act = () => Create().ExchangeAsync(BuildContext().Request);

            await act.Should().ThrowAsync<TimeoutException>();
            _tokenService.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_ChallengeStoredButNoVerifier_ReturnsInvalidGrant()
        {
            SetupValidExchange();
            var authCode = ValidAuthCode();
            authCode.CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);

            var result = await Create().ExchangeAsync(BuildContext(codeVerifier: "").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("PKCE code_verifier is required");
            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
        }

        private static OidcClientRegistration ConfidentialClient() => new()
        {
            ItemId = "cid", ClientId = "cid", UseTokensCookie = true,
            ClientType = "confidential", TokenEndpointAuthMethod = "client_secret_post", ClientSecret = "s3cret"
        };

        [Theory]
        [InlineData("")]
        [InlineData("wrong")]
        public async Task Exchange_ConfidentialClient_BadOrMissingSecret_ReturnsInvalidClient(string secret)
        {
            SetupValidExchange(ConfidentialClient());

            var result = await Create().ExchangeAsync(BuildContext(clientSecret: secret).Request);

            var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            Prop(unauthorized.Value, "error").Should().Be("invalid_client");
            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_ConfidentialClient_SecretInForm_Passes()
        {
            SetupValidExchange(ConfidentialClient());

            var result = await Create().ExchangeAsync(BuildContext(clientSecret: "s3cret").Request);

            result.Should().NotBeOfType<UnauthorizedObjectResult>();
            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.Is<AuthorizationCodeModel>(c => c.Code == "auth-code")), Times.Once);
        }

        [Fact]
        public async Task Exchange_ConfidentialClient_SecretInBasicHeader_Passes()
        {
            SetupValidExchange(ConfidentialClient());

            var result = await Create().ExchangeAsync(BuildContext(clientId: "", basicAuth: "cid:s3cret").Request);

            result.Should().NotBeOfType<UnauthorizedObjectResult>();
            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.Is<AuthorizationCodeModel>(c => c.Code == "auth-code")), Times.Once);
        }

        [Fact]
        public async Task Exchange_PublicClient_CodeWithoutPkce_ReturnsInvalidGrant()
        {
            SetupValidExchange(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", ClientType = "public", TokenEndpointAuthMethod = "none" });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("PKCE is required for public clients");
            _authCodeRepo.Verify(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>()), Times.Never);
        }

        // ---------- blocks-iam#617: native app (app-scheme) public clients ----------

        private const string AppRedirectUri = "com.klaxai.app://login/callback";
        private const string AppChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        private void SetupAppSchemeExchange()
        {
            SetupValidExchange(new OidcClientRegistration
            {
                ItemId = "cid", ClientId = "cid", ClientType = "public", TokenEndpointAuthMethod = "none",
                RequirePkce = true, UseTokensCookie = false, RedirectUris = new List<string> { AppRedirectUri }
            });
            var authCode = ValidAuthCode();
            authCode.RedirectUri = AppRedirectUri;
            authCode.CodeChallenge = AppChallenge;
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);
            _pkce.Setup(p => p.ValidateVerifierAsync(AppChallenge, "right-verifier", "S256")).ReturnsAsync(true);
            _pkce.Setup(p => p.ValidateVerifierAsync(AppChallenge, "wrong-verifier", "S256")).ReturnsAsync(false);
        }

        [Fact]
        public async Task Exchange_AppSchemePublicClient_VerifierAndSameRedirect_NoSecret_IssuesTokensInTheBody()
        {
            SetupAppSchemeExchange();

            var result = await Create().ExchangeAsync(
                BuildContext(codeVerifier: "right-verifier", redirectUri: AppRedirectUri).Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "access_token").Should().Be("access-token");
            Prop(ok.Value, "refresh_token").Should().NotBeNull();
        }

        [Theory]
        [InlineData("", AppRedirectUri)]
        [InlineData("wrong-verifier", AppRedirectUri)]
        [InlineData("right-verifier", "com.klaxai.app://login/callback/")]
        [InlineData("right-verifier", "https://app/cb")]
        public async Task Exchange_AppSchemePublicClient_MissingOrWrongVerifierOrOtherRedirect_IsInvalidGrant(
            string verifier, string redirectUri)
        {
            SetupAppSchemeExchange();

            var result = await Create().ExchangeAsync(BuildContext(codeVerifier: verifier, redirectUri: redirectUri).Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            _tokenService.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthorizationCodeExchangeService Create() =>
            new(_authCodeRepo.Object, _refreshTokenRepo.Object, _tokenService.Object, _pkce.Object,
                _userRepo.Object, _claimsResolver.Object, _repo.Object, _tenants.Object, _tokenRevocation.Object, _cache.Object,
                NullLogger<AuthorizationCodeExchangeService>.Instance);

        private static DefaultHttpContext BuildContext(
            string code = "auth-code", string codeVerifier = "", string clientId = "cid",
            string redirectUri = "https://app/cb", string tenantId = "", string? origin = null,
            string clientSecret = "", string? basicAuth = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["tenant_id"] = tenantId,
                ["client_secret"] = clientSecret
            });
            if (basicAuth != null)
            {
                ctx.Request.Headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(basicAuth));
            }
            if (origin != null)
            {
                ctx.Request.Headers["Origin"] = origin;
            }
            return ctx;
        }

        private static AuthorizationCodeModel ValidAuthCode() => new()
        {
            Code = "auth-code", ClientId = "cid", TenantId = "tenant-1", UserId = "user-1",
            OrganizationId = "default", RedirectUri = "https://app/cb", Scope = "openid profile",
            CodeChallenge = "", CodeChallengeMethod = "S256", Amr = new(), ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IdpSessionId = "sess-from-code"
        };

        private static User ValidUser() => new()
        {
            ItemId = "user-1", FirstName = "Jane", LastName = "Doe", UserName = "jane", Email = "jane@x.com"
        };

        private void SetupValidExchange(OidcClientRegistration? registration = null)
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(registration ?? new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = true });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(ValidUser());
        }

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        // ---------- ValidateInputsAsync branches ----------

        [Fact]
        public async Task Exchange_MissingCode_ReturnsInvalidRequest()
        {
            var result = await Create().ExchangeAsync(BuildContext(code: "").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task Exchange_AuthCodeNotFound_ReturnsInvalidGrant()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync((AuthorizationCodeModel)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
        }

        [Fact]
        public async Task Exchange_TenantMismatch_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.TenantId = "tenant-other";
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);

            var result = await Create().ExchangeAsync(BuildContext(tenantId: "tenant-1").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            Prop(bad.Value, "error_description").Should().Be("Tenant mismatch");
        }

        [Fact]
        public async Task Exchange_ExpiredCode_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("Authorization code has expired");
        }

        [Fact]
        public async Task Exchange_ClientNotFound_ReturnsInvalidClient()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task Exchange_ClientIdMismatch_ReturnsInvalidClient()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "different-client" });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task Exchange_RedirectUriMismatch_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.RedirectUri = "https://attacker/cb";
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });

            var result = await Create().ExchangeAsync(BuildContext(redirectUri: "https://app/cb").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("Redirect URI mismatch");
        }

        [Fact]
        public async Task Exchange_PkceInvalid_ReturnsInvalidGrant()
        {
            SetupValidExchange();
            _pkce.Setup(p => p.ValidateVerifierAsync(It.IsAny<string>(), "bad-verifier", It.IsAny<string>())).ReturnsAsync(false);

            var result = await Create().ExchangeAsync(BuildContext(codeVerifier: "bad-verifier").Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("PKCE code_verifier is invalid");
        }

        [Fact]
        public async Task Exchange_UserNotFound_ReturnsInvalidGrant()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync((User)null!);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error_description").Should().Be("User not found");
        }

        [Fact]
        public async Task Exchange_UserLocked_Returns423()
        {
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(ValidAuthCode());
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            var locked = ValidUser();
            locked.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(locked);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
            Prop(obj.Value, "error").Should().Be("account_locked");
        }

        [Fact]
        public async Task Exchange_UsesAuthorizationCodeSessionId_WhenPresent()
        {
            var authCode = ValidAuthCode();
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = false });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(ValidUser());

            var result = await Create().ExchangeAsync(BuildContext().Request);

            result.Should().BeOfType<OkObjectResult>();
            _tokenService.Verify(t => t.GenerateRefreshTokenAsync(
                It.IsAny<OidcClaims>(),
                It.IsAny<string>(),
                false,
                "sess-from-code"), Times.Once);
        }

        [Fact]
        public async Task Exchange_MissingAuthorizationCodeSessionId_ReturnsInvalidGrant()
        {
            var authCode = ValidAuthCode();
            authCode.IdpSessionId = null;
            _authCodeRepo.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(authCode);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = false });
            _userRepo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(ValidUser());

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_grant");
            Prop(bad.Value, "error_description").Should().Be("Authorization code is missing IdP session");
            _tokenService.Verify(t => t.GenerateRefreshTokenAsync(
                It.IsAny<OidcClaims>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>()), Times.Never);
        }

        // ---------- ExchangeCoreAsync branches ----------

        [Fact]
        public async Task Exchange_HappyPath_NoResolvableDomain_ReturnsTokensInBody()
        {
            SetupValidExchange();

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "access_token").Should().Be("access-token");
            Prop(ok.Value, "id_token").Should().Be("id-token");
            Prop(ok.Value, "refresh_token").Should().Be("rt-1");
            Prop(ok.Value, "cookie_set").Should().Be(false);
        }

        [Fact]
        public async Task Exchange_AccessTokenEmpty_ReturnsServerError()
        {
            SetupValidExchange();
            _tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(string.Empty);

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("server_error");
        }

        [Fact]
        public async Task Exchange_UseTokensCookieFalse_ReturnsTokensInBody()
        {
            SetupValidExchange(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = false });

            var result = await Create().ExchangeAsync(BuildContext().Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "cookie_set").Should().Be(false);
            Prop(ok.Value, "access_token").Should().Be("access-token");
        }

        [Fact]
        public async Task Exchange_HappyPath_WithResolvedDomain_SetsCookies()
        {
            SetupValidExchange();
            var tenant = new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = "",
                Applications = new List<Applications>
                {
                    new() { Domain = "app.example.com", CookieDomain = ".example.com", IsDomainVerified = true }
                },
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
            };
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(tenant);

            var ctx = BuildContext(origin: "https://app.example.com");
            var result = await Create().ExchangeAsync(ctx.Request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "cookie_set").Should().Be(true);
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }
    }
}
