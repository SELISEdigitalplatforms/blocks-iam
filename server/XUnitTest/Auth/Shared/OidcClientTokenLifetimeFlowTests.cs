using System.Security.Claims;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// iam SPEC5: per-OIDC-client token lifetimes through the admin API and every token issue path.
    /// </summary>
    public sealed class OidcClientTokenLifetimeFlowTests : IDisposable
    {
        private static readonly OidcClientTokenLifetimes Custom = new()
        {
            AccessTokenValidForNumberMinutes = 30,
            RefreshTokenValidForNumberMinutes = 1440,
            AbsoluteRefreshTokenValidForNumberMinutes = 43200
        };

        private readonly Mock<IOidcClientTokenLifetimeService> _lifetimes = new();

        public OidcClientTokenLifetimeFlowTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static IdentityConfiguration Project() => new()
        {
            RefreshTokenValidForNumberMinutes = 30,
            AbsoluteRefreshTokenValidForNumberMinutes = 10080
        };

        /// <summary>The real overlay behind a mocked store: "cid" has <see cref="Custom"/>, every other client none.</summary>
        private void ClientHasCustomLifetimes(string clientId = "cid", OidcClientTokenLifetimes? lifetimes = null)
        {
            var value = lifetimes ?? Custom;
            _lifetimes.Setup(l => l.ApplyAsync(It.IsAny<IdentityConfiguration?>(), It.IsAny<string?>()))
                .ReturnsAsync((IdentityConfiguration? project, string? id) =>
                    string.Equals(id, clientId, StringComparison.Ordinal)
                        ? OidcClientTokenLifetimeService.Apply(project, value)
                        : project);
        }

        private static Tenant MakeTenant() => new()
        {
            TenantId = "tenant-1",
            Name = "t",
            DbConnectionString = string.Empty,
            Applications = new List<Applications>(),
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
        };

        private static User MakeUser() => new() { ItemId = "user-1", TokenVersion = 1, UserName = "u", Email = "u@test.local" };

        // =====================================================================================
        // Admin API: AuthenticationDomainService save / get / delete
        // =====================================================================================

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcValidator = new();

        private AuthenticationDomainService DomainService(bool withLifetimes = true)
        {
            _oidcValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync((IdentityProvider)null!);
            return new AuthenticationDomainService(
                new Mock<IMessageClient>().Object, _repo.Object,
                new AuthenticationRequestValidators(_oidcValidator.Object,
                    new Mock<IValidator<SaveOidcUiTemplateRequest>>().Object,
                    new Mock<IValidator<SaveIdentityProviderRequest>>().Object,
                    new Mock<IValidator<UpdateIdentityProviderRequest>>().Object),
                new Mock<IHttpClientFactory>().Object,
                withLifetimes ? _lifetimes.Object : null,
                NullLogger<AuthenticationDomainService>.Instance);
        }

        private void ExistingClient(string id = "cid") =>
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(id))
                .ReturnsAsync(new OidcClientRegistration { ItemId = id, ClientId = id, ClientSecret = "s" });

        private void NoExistingClient() =>
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration)null!);

        private static SaveOIDCClientRequest SaveRequest(OidcClientTokenLifetimes? lifetimes, string? itemId = "cid") => new()
        {
            ItemId = itemId,
            RedirectUris = ["https://app.example.com/cb"],
            AllowedScopes = ["openid"],
            RegisterAsIdentityProvider = false,
            TokenLifetimes = lifetimes
        };

        [Fact]
        public async Task Save_WithLifetimes_StoresThemAfterTheClient()
        {
            ExistingClient();
            var order = new List<string>();
            _repo.Setup(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()))
                .Callback(() => order.Add("client")).Returns(Task.CompletedTask);
            _lifetimes.Setup(l => l.SaveAsync("cid", It.IsAny<OidcClientTokenLifetimes>()))
                .Callback(() => order.Add("lifetimes")).Returns(Task.CompletedTask);

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(Custom));

            result.IsSuccess.Should().BeTrue();
            order.Should().Equal("client", "lifetimes");
            _lifetimes.Verify(l => l.SaveAsync("cid", It.Is<OidcClientTokenLifetimes>(v =>
                v.AccessTokenValidForNumberMinutes == 30 && v.RefreshTokenValidForNumberMinutes == 1440 && v.AbsoluteRefreshTokenValidForNumberMinutes == 43200)), Times.Once);
        }

        [Fact]
        public async Task Save_ExistingClientWithoutLifetimes_KeepsStoredValues()
        {
            ExistingClient();

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(null));

            result.IsSuccess.Should().BeTrue();
            _lifetimes.Verify(l => l.SaveAsync(It.IsAny<string>(), It.IsAny<OidcClientTokenLifetimes>()), Times.Never);
            _lifetimes.Verify(l => l.DeleteAsync(It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.GetAuthenticationConfigurationAsync(), Times.Never);
        }

        [Fact]
        public async Task Save_NewClientWithoutLifetimes_RemovesAnyLeftoverEntry()
        {
            NoExistingClient();

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(null, itemId: "reused-id"));

            result.IsSuccess.Should().BeTrue();
            _lifetimes.Verify(l => l.DeleteAsync("reused-id"), Times.Once);
        }

        [Fact]
        public async Task Save_AllNullLifetimes_IsPassedOnSoTheEntryIsCleared()
        {
            ExistingClient();

            await DomainService().SaveOIDCClientAsync(SaveRequest(new OidcClientTokenLifetimes()));

            _lifetimes.Verify(l => l.SaveAsync("cid", It.Is<OidcClientTokenLifetimes>(v => !v.HasAnyValue())), Times.Once);
        }

        [Fact]
        public async Task Save_LifetimeStoreFails_ReportsTokenLifetimesError_ClientStaysSaved()
        {
            ExistingClient();
            _lifetimes.Setup(l => l.SaveAsync(It.IsAny<string>(), It.IsAny<OidcClientTokenLifetimes>()))
                .ThrowsAsync(new TimeoutException("store unavailable"));

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(Custom));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("TokenLifetimes")
                .WhoseValue.Should().Be(AuthenticationDomainService.TokenLifetimesSaveFailedMessage);
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Once);
        }

        [Fact]
        public async Task Save_ValidationFails_WritesNothing()
        {
            ExistingClient();
            var service = DomainService();
            _oidcValidator.Setup(v => v.ValidateAsync(It.IsAny<SaveOIDCClientRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("TokenLifetimes.AccessTokenValidForNumberMinutes", "bad") }));

            var result = await service.SaveOIDCClientAsync(SaveRequest(new OidcClientTokenLifetimes { AccessTokenValidForNumberMinutes = 0 }));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("TokenLifetimes.AccessTokenValidForNumberMinutes");
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
            _lifetimes.Verify(l => l.SaveAsync(It.IsAny<string>(), It.IsAny<OidcClientTokenLifetimes>()), Times.Never);
        }

        [Fact]
        public async Task Save_OnlySlidingSet_AboveProjectMaximum_IsRejected_WritesNothing()
        {
            ExistingClient();
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(Project());

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(new OidcClientTokenLifetimes { RefreshTokenValidForNumberMinutes = 20000 }));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("TokenLifetimes.RefreshTokenValidForNumberMinutes")
                .WhoseValue.Should().Contain("10080 minutes");
            _repo.Verify(r => r.SaveOidcClientRegistrationAsync(It.IsAny<OidcClientRegistration>()), Times.Never);
            _lifetimes.Verify(l => l.SaveAsync(It.IsAny<string>(), It.IsAny<OidcClientTokenLifetimes>()), Times.Never);
        }

        [Fact]
        public async Task Save_OnlyAbsoluteSet_BelowProjectIdle_IsRejected()
        {
            ExistingClient();
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(Project());

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(new OidcClientTokenLifetimes { AbsoluteRefreshTokenValidForNumberMinutes = 20 }));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("TokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes")
                .WhoseValue.Should().Contain("30 minutes");
        }

        [Fact]
        public async Task Save_OnlySlidingSet_WithinProjectMaximum_IsAccepted()
        {
            ExistingClient();
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(Project());

            var result = await DomainService().SaveOIDCClientAsync(SaveRequest(new OidcClientTokenLifetimes { RefreshTokenValidForNumberMinutes = 600 }));

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Save_WithoutLifetimeService_BehavesAsBefore()
        {
            ExistingClient();

            var result = await DomainService(withLifetimes: false).SaveOIDCClientAsync(SaveRequest(Custom));

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Get_ReturnsStoredLifetimes_NextToTheClient()
        {
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _lifetimes.Setup(l => l.GetAsync("cid")).ReturnsAsync(Custom);

            var result = await DomainService().GetOidcClientAsync("cid");

            result.oIDCClientCredential!.ClientId.Should().Be("cid");
            result.TokenLifetimes.Should().BeSameAs(Custom);
        }

        [Fact]
        public async Task Get_ClientWithoutLifetimes_ReturnsNull()
        {
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync("cid")).ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid" });
            _lifetimes.Setup(l => l.GetAsync("cid")).ReturnsAsync((OidcClientTokenLifetimes?)null);

            (await DomainService().GetOidcClientAsync("cid")).TokenLifetimes.Should().BeNull();
        }

        [Fact]
        public async Task Get_UnknownClient_DoesNotReadLifetimes()
        {
            _repo.Setup(r => r.GetOIDCCredentialByIdAsync("nope")).ReturnsAsync((OidcClientRegistration)null!);

            var result = await DomainService().GetOidcClientAsync("nope");

            result.oIDCClientCredential.Should().BeNull();
            result.TokenLifetimes.Should().BeNull();
            _lifetimes.Verify(l => l.GetAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Delete_RemovesLifetimesBeforeTheClient()
        {
            ExistingClient();
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());
            var order = new List<string>();
            _lifetimes.Setup(l => l.DeleteAsync("cid")).Callback(() => order.Add("lifetimes")).Returns(Task.CompletedTask);
            _repo.Setup(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>())).Callback(() => order.Add("client")).Returns(Task.CompletedTask);

            var result = await DomainService().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "cid" });

            result.IsSuccess.Should().BeTrue();
            order.Should().Equal("lifetimes", "client");
        }

        [Fact]
        public async Task Delete_LifetimeDeleteFails_ClientIsNotDeleted()
        {
            ExistingClient();
            _lifetimes.Setup(l => l.DeleteAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("store unavailable"));

            await Assert.ThrowsAsync<TimeoutException>(() => DomainService().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "cid" }));

            _repo.Verify(r => r.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>()), Times.Never);
        }

        // =====================================================================================
        // Refresh token issue and rotation: UnifiedTokenSessionService (P6)
        // =====================================================================================

        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private RefreshTokenModel? _persisted;
        private long _cacheTtl = -1;

        private UnifiedTokenSessionService Sessions(bool withLifetimes = true)
        {
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation { Device = "Test" });
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("session-1");
            _refreshRepo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()))
                .Callback<RefreshTokenModel>(m => _persisted = m)
                .ReturnsAsync("ok");
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, _, ttl) => _cacheTtl = ttl)
                .ReturnsAsync(true);

            return new UnifiedTokenSessionService(
                _cache.Object, authDomain.Object, _refreshRepo.Object, new Mock<IUserActivityDispatcher>().Object,
                idpSession.Object, new Mock<IHttpContextAccessor>().Object, new Mock<IUserRepository>().Object,
                NullLogger<UnifiedTokenSessionService>.Instance,
                withLifetimes ? _lifetimes.Object : null);
        }

        private static TokenRequest Request(string? clientId, string grantType = "authorization_code") =>
            new() { ClientId = clientId, GrantType = grantType, Scope = "openid", IdpSessionId = "session-1" };

        [Fact]
        public async Task FirstIssue_ClientWithLifetimes_UsesThemForSlidingAbsoluteAndTtl()
        {
            ClientHasCustomLifetimes();
            var before = DateTime.UtcNow;

            var result = await Sessions().CreateOrRotateRefreshToken(RefreshTokenPredecessor.None, Request("cid"), Project(), MakeTenant(), MakeUser(), ["127.0.0.1"], false);

            result.SlidingExpiry.Should().BeCloseTo(before.AddMinutes(1440), TimeSpan.FromSeconds(5));
            result.AbsoluteExpiry.Should().BeCloseTo(before.AddMinutes(43200), TimeSpan.FromSeconds(5));
            _persisted!.SlidingExpiry.Should().Be(result.SlidingExpiry);
            _persisted.AbsoluteExpiry.Should().Be(result.AbsoluteExpiry);
            _cacheTtl.Should().Be(1440 * 60);
        }

        [Fact]
        public async Task FirstIssue_ClientWithoutLifetimes_KeepsProjectValues_AndPassesTheSameInstance()
        {
            var project = Project();
            _lifetimes.Setup(l => l.ApplyAsync(It.IsAny<IdentityConfiguration?>(), It.IsAny<string?>()))
                .ReturnsAsync((IdentityConfiguration? p, string? _) => p);
            var before = DateTime.UtcNow;

            var result = await Sessions().CreateOrRotateRefreshToken(RefreshTokenPredecessor.None, Request("other"), project, MakeTenant(), MakeUser(), ["127.0.0.1"], false);

            result.SlidingExpiry.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(5));
            result.AbsoluteExpiry.Should().BeCloseTo(before.AddMinutes(10080), TimeSpan.FromSeconds(5));
            _cacheTtl.Should().Be(30 * 60);
            _lifetimes.Verify(l => l.ApplyAsync(It.Is<IdentityConfiguration?>(c => ReferenceEquals(c, project)), "other"), Times.Once);
        }

        [Fact]
        public async Task FirstIssue_WithoutLifetimeService_MatchesProjectValues()
        {
            var before = DateTime.UtcNow;

            var result = await Sessions(withLifetimes: false).CreateOrRotateRefreshToken(RefreshTokenPredecessor.None, Request("cid"), Project(), MakeTenant(), MakeUser(), ["127.0.0.1"], false);

            result.SlidingExpiry.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(5));
            result.AbsoluteExpiry.Should().BeCloseTo(before.AddMinutes(10080), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Rotation_KeepsTheLineageAbsoluteExpiry_AndUsesTheLineageClientsSlidingValue()
        {
            ClientHasCustomLifetimes();
            var lineageAbsolute = DateTime.UtcNow.AddDays(3);
            var old = new RefreshTokenCache
            {
                RefreshToken = "old-token", TenantId = "tenant-1", ClientId = "cid", SessionId = "session-1",
                RefreshTokenSessionId = "lineage-1", UserId = "user-1", IssuedUtc = DateTime.UtcNow.AddHours(-1),
                ExpiresUtc = DateTime.UtcNow.AddMinutes(5), AbsoluteExpiresUtc = lineageAbsolute
            };
            var before = DateTime.UtcNow;

            // The request names another client; the lineage client still decides (existing lineage rule).
            var result = await Sessions().CreateOrRotateRefreshToken(new RefreshTokenPredecessor("old-token", old), Request("someone-else", GrantTypes.RefreshToken), Project(), MakeTenant(), MakeUser(), ["127.0.0.1"], false);

            result.AbsoluteExpiry.Should().Be(lineageAbsolute);
            result.SlidingExpiry.Should().BeCloseTo(before.AddMinutes(1440), TimeSpan.FromSeconds(5));
            _persisted!.ClientId.Should().Be("cid");
            _lifetimes.Verify(l => l.ApplyAsync(It.IsAny<IdentityConfiguration?>(), "cid"), Times.Once);
            _cacheTtl.Should().BeLessThanOrEqualTo(1440 * 60);
        }

        [Fact]
        public async Task Rotation_ClientAbsoluteChangedAfterIssue_DoesNotMoveTheLineageCap()
        {
            ClientHasCustomLifetimes(lifetimes: new OidcClientTokenLifetimes { AbsoluteRefreshTokenValidForNumberMinutes = 525600 });
            var lineageAbsolute = DateTime.UtcNow.AddHours(2);
            var old = new RefreshTokenCache
            {
                RefreshToken = "old-token", TenantId = "tenant-1", ClientId = "cid", SessionId = "session-1",
                RefreshTokenSessionId = "lineage-1", UserId = "user-1", AbsoluteExpiresUtc = lineageAbsolute
            };

            var result = await Sessions().CreateOrRotateRefreshToken(new RefreshTokenPredecessor("old-token", old), Request("cid", GrantTypes.RefreshToken), Project(), MakeTenant(), MakeUser(), ["127.0.0.1"], false);

            result.AbsoluteExpiry.Should().Be(lineageAbsolute);
            _cacheTtl.Should().BeLessThanOrEqualTo(2 * 60 * 60);
        }

        // =====================================================================================
        // Redis re-hydrate after a cache miss: RefreshSessionResolver (P7)
        // =====================================================================================

        [Fact]
        public async Task Rehydrate_UsesTheTokenClientsSlidingValue_ForTheTtl()
        {
            ClientHasCustomLifetimes(lifetimes: new OidcClientTokenLifetimes { RefreshTokenValidForNumberMinutes = 120 });
            var ttl = -1L;
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);
            _cache.Setup(c => c.AddStringValueAsync("A", It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, _, seconds) => ttl = seconds)
                .ReturnsAsync(true);
            _refreshRepo.Setup(r => r.GetByTokenIdAsync("A")).ReturnsAsync(new RefreshTokenModel
            {
                TokenId = "A", UserId = "user-1", TenantId = "tenant-1", ClientId = "cid", SessionId = "session-1",
                RefreshTokenSessionId = "lineage-1", IssuedUtc = DateTime.UtcNow.AddMinutes(-10),
                SlidingExpiry = DateTime.UtcNow.AddMinutes(100), AbsoluteExpiry = DateTime.UtcNow.AddDays(6)
            });

            var resolver = new RefreshSessionResolver(_cache.Object, _refreshRepo.Object, NullLogger<RefreshSessionResolver>.Instance, _lifetimes.Object);
            var result = await resolver.TryResolveRefreshSessionAsync("A", Project());

            result.Should().NotBeNull();
            ttl.Should().Be(120 * 60);
        }

        // =====================================================================================
        // Access tokens: OIDC refresh grant (P5), session mint (P4), device code mint (P2), code exchange (P1)
        // =====================================================================================

        private readonly Mock<IJwtAccessTokenProvider> _jwtProvider = new();
        private IdentityConfiguration? _accessConfiguration;

        private void CaptureAccessConfiguration() =>
            _jwtProvider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .Callback<IdentityConfiguration, Tenant, User, TokenRequest, StateInfo>((c, _, _, _, _) => _accessConfiguration = c)
                .ReturnsAsync(() => new JwtAccessToken
                {
                    Issuer = "https://issuer",
                    Audience = "aud",
                    Claims = new List<Claim> { new(BlocksContext.SUBJECT_CLAIM, "blocks|u1") },
                    NotBefore = DateTime.UtcNow,
                    Expires = DateTime.UtcNow.AddMinutes(_accessConfiguration!.AccessTokenValidForNumberMinutes),
                    SigningCredentials = XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.RsaSigningCredentials()
                });

        [Fact]
        public async Task OidcRefreshGrant_AccessTokenUsesTheClientValue_RefreshGetsTheProjectInstance()
        {
            ClientHasCustomLifetimes();
            CaptureAccessConfiguration();
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            var manager = new Mock<IOAuthJwtAccessTokenManager>();
            IdentityConfiguration? refreshConfiguration = null;
            manager.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .Callback<TokenRequest, JwtAccessToken, IdentityConfiguration, Tenant, User>((_, _, c, _, _) => refreshConfiguration = c)
                .ReturnsAsync(("new-token", DateTime.UtcNow.AddDays(30)));
            var project = Project();

            var service = new RefreshTokenAuthenticationService(
                NullLogger<RefreshTokenAuthenticationService>.Instance, _jwtProvider.Object, tenants.Object, manager.Object, _repo.Object, _lifetimes.Object);
            var response = await service.AuthenticateAsync(
                new TokenRequest { GrantType = GrantTypes.RefreshToken, ClientId = "cid", RefreshToken = "old", Request = new DefaultHttpContext().Request },
                project, MakeUser());

            response.ExpiresIn.Should().Be(1800);
            _accessConfiguration!.AccessTokenValidForNumberMinutes.Should().Be(30);
            refreshConfiguration.Should().BeSameAs(project);
        }

        [Fact]
        public async Task OidcRefreshGrant_ClientWithoutLifetimes_UsesTheProjectInstanceForTheAccessToken()
        {
            ClientHasCustomLifetimes(clientId: "cid");
            CaptureAccessConfiguration();
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            var manager = new Mock<IOAuthJwtAccessTokenManager>();
            manager.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .ReturnsAsync(("new-token", DateTime.UtcNow.AddDays(30)));
            var project = Project();

            var service = new RefreshTokenAuthenticationService(
                NullLogger<RefreshTokenAuthenticationService>.Instance, _jwtProvider.Object, tenants.Object, manager.Object, _repo.Object, _lifetimes.Object);
            var response = await service.AuthenticateAsync(
                new TokenRequest { GrantType = GrantTypes.RefreshToken, ClientId = "plain-client", RefreshToken = "old", Request = new DefaultHttpContext().Request },
                project, MakeUser());

            _accessConfiguration.Should().BeSameAs(project);
            response.ExpiresIn.Should().Be(IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes * 60);
        }

        [Fact]
        public async Task SessionMint_AccessAndRefreshFollowTheClient()
        {
            ClientHasCustomLifetimes();
            CaptureAccessConfiguration();
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.IsSessionActiveAsync("session-1")).ReturnsAsync(true);
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(["127.0.0.1"]);

            var manager = new OAuthJwtAccessTokenManager(
                _jwtProvider.Object, authDomain.Object, _repo.Object, new Mock<IMfaPolicyService>().Object, _cache.Object,
                tenants.Object, new Mock<IOtpServiceFactory>().Object, Sessions(), idpSession.Object, _lifetimes.Object);
            var before = DateTime.UtcNow;

            var response = await manager.ManageTokenAsync(
                new TokenRequest { GrantType = GrantTypes.MfaCode, ClientId = "cid", IdpSessionId = "session-1", Request = new DefaultHttpContext().Request },
                Project(), MakeUser());

            response.Error.Should().BeNullOrEmpty();
            response.ExpiresIn.Should().Be(1800);
            _accessConfiguration!.AccessTokenValidForNumberMinutes.Should().Be(30);
            _persisted!.SlidingExpiry.Should().BeCloseTo(before.AddMinutes(1440), TimeSpan.FromSeconds(5));
            _persisted.AbsoluteExpiry.Should().BeCloseTo(before.AddMinutes(43200), TimeSpan.FromSeconds(5));
            response.RefreshExpiresUtc.Should().Be(_persisted.AbsoluteExpiry);
        }

        [Fact]
        public async Task SessionMint_NoClientId_UsesTheProjectInstance()
        {
            _lifetimes.Setup(l => l.ApplyAsync(It.IsAny<IdentityConfiguration?>(), It.IsAny<string?>()))
                .ReturnsAsync((IdentityConfiguration? p, string? _) => p);
            CaptureAccessConfiguration();
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.IsSessionActiveAsync("session-1")).ReturnsAsync(true);
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(["127.0.0.1"]);
            var project = Project();

            var manager = new OAuthJwtAccessTokenManager(
                _jwtProvider.Object, authDomain.Object, _repo.Object, new Mock<IMfaPolicyService>().Object, _cache.Object,
                tenants.Object, new Mock<IOtpServiceFactory>().Object, Sessions(), idpSession.Object, _lifetimes.Object);

            var response = await manager.ManageTokenAsync(
                new TokenRequest { GrantType = GrantTypes.MfaCode, ClientId = null, IdpSessionId = "session-1", Request = new DefaultHttpContext().Request },
                project, MakeUser());

            _accessConfiguration.Should().BeSameAs(project);
            response.ExpiresIn.Should().Be(IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes * 60);
        }

        [Fact]
        public async Task DeviceCodeMint_UsesTheClientAccessValue()
        {
            ClientHasCustomLifetimes();
            var tokenService = new Mock<ITokenGenerationService>();
            var seconds = new List<int>();
            tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((_, _, s) => seconds.Add(s)).ReturnsAsync("id-token");
            tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((_, _, s) => seconds.Add(s)).ReturnsAsync("access-token");
            tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            var claims = new Mock<IAuthorizationClaimsResolver>();
            claims.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = new() { "admin" }, Permissions = new() { "read" } });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(Project());
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var mint = new OidcTokenMintService(tokenService.Object, claims.Object, _repo.Object, tenants.Object,
                new Mock<IIdpSessionService>().Object, NullLogger<OidcTokenMintService>.Instance, _lifetimes.Object);
            var result = await mint.MintAsync(new OidcTokenMintRequest
            {
                User = MakeUser(), TenantId = "tenant-1", OrganizationId = "default", Scope = "openid", ClientId = "cid"
            });

            result.ExpiresIn.Should().Be(1800);
            seconds.Should().Equal(1800, 1800);
        }

        [Fact]
        public async Task AuthorizationCodeExchange_UsesTheClientAccessValue_ForAccessAndIdTokens()
        {
            ClientHasCustomLifetimes();
            var authCodes = new Mock<IAuthorizationCodeRepository>();
            authCodes.Setup(r => r.GetByCodeAsync("auth-code")).ReturnsAsync(new AuthorizationCodeModel
            {
                Code = "auth-code", ClientId = "cid", TenantId = "tenant-1", UserId = "user-1",
                OrganizationId = "default", RedirectUri = "https://app/cb", Scope = "openid profile",
                CodeChallenge = "", CodeChallengeMethod = "S256", Amr = new(), ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                IdpSessionId = "sess-from-code"
            });
            authCodes.Setup(r => r.TryConsumeAsync(It.IsAny<AuthorizationCodeModel>())).ReturnsAsync(AuthorizationCodeConsumeResult.Consumed);
            authCodes.Setup(r => r.RecordMintAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<AuthorizationCodeMint>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid"))
                .ReturnsAsync(new OidcClientRegistration { ItemId = "cid", ClientId = "cid", UseTokensCookie = true });
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(Project());
            var users = new Mock<IUserRepository>();
            users.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1", FirstName = "Jane", LastName = "Doe", UserName = "jane", Email = "jane@x.com" });
            var claims = new Mock<IAuthorizationClaimsResolver>();
            claims.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = new() { "admin" }, Permissions = new() { "read" } });
            var tokenService = new Mock<ITokenGenerationService>();
            var seconds = new List<int>();
            tokenService.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((_, _, s) => seconds.Add(s)).ReturnsAsync("id-token");
            tokenService.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<OidcClaims, string, int>((_, _, s) => seconds.Add(s)).ReturnsAsync("access-token");
            tokenService.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(30) });
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var exchange = new AuthorizationCodeExchangeService(
                authCodes.Object, _refreshRepo.Object, tokenService.Object, new Mock<IPkceService>().Object, users.Object,
                claims.Object, _repo.Object, tenants.Object, new Mock<ITokenRevocationRepository>().Object, _cache.Object,
                NullLogger<AuthorizationCodeExchangeService>.Instance, _lifetimes.Object);

            var context = new DefaultHttpContext();
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["code"] = "auth-code", ["code_verifier"] = "", ["client_id"] = "cid",
                ["redirect_uri"] = "https://app/cb", ["tenant_id"] = "", ["client_secret"] = ""
            });

            var result = await exchange.ExchangeAsync(context.Request);

            result.Should().BeOfType<OkObjectResult>();
            seconds.Should().Equal(1800, 1800);
            _lifetimes.Verify(l => l.ApplyAsync(It.IsAny<IdentityConfiguration?>(), "cid"), Times.Once);
        }
    }
}
