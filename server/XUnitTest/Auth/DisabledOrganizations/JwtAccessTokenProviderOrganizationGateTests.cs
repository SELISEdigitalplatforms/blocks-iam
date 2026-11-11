using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668: <see cref="JwtAccessTokenProvider.ResolveOrganizationAsync"/> (Q1 write-back, refusal leaves the
    /// request untouched, C12 impersonation), the <c>org_status</c> claim (H4) and the <see cref="JwtAccessTokenProvider.GetJwtAccessToken"/> backstop.
    /// </summary>
    public sealed class JwtAccessTokenProviderOrganizationGateTests
    {
        private const string CertPassword = "pwd";
        private const string A = "org-a";
        private const string D = "org-d";

        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<ICertificateProviderFactory> _certificateProviderFactory = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IResourceRepository> _resourceRepository = new();

        public JwtAccessTokenProviderOrganizationGateTests()
        {
            _cacheClient.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _cryptoService.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("cache-key");
            _certificateProviderFactory.Setup(f => f.GetProvider(It.IsAny<CertificateStorageType>())).Returns(new Mock<ICertificateProvider>().Object);
            _cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(GenerateCertificate());
            _claimsResolver.Setup(r => r.ResolveAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims());
            MultiOrg(allowNoOrg: false);
        }

        private JwtAccessTokenProvider Sut(IAuthorizationClaimsResolver? claimsResolver = null) => new(
            NullLogger<JwtAccessTokenProvider>.Instance, _cacheClient.Object, _cryptoService.Object,
            _certificateProviderFactory.Object, claimsResolver ?? _claimsResolver.Object, _resourceRepository.Object);

        private void MultiOrg(bool allowNoOrg, bool enabled = true) =>
            _resourceRepository.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = enabled, AllowNoOrgSignInForDisabledOrganizations = allowNoOrg });

        private static byte[] GenerateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, CertPassword);
        }

        private static Tenant MakeTenant() => new()
        {
            TenantId = "tenant-1",
            ItemId = "tenant-item-1",
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://issuer",
                PrivateCertificatePassword = CertPassword,
                IssueDate = DateTime.UtcNow,
                CertificateStorageType = CertificateStorageType.Azure
            }
        };

        private static IdentityConfiguration Config() => new() { AccessTokenValidForNumberMinutes = 10, RefreshTokenValidForNumberMinutes = 60 };

        // ---------- ResolveOrganizationAsync ----------

        [Fact]
        public async Task Q1_Issued_RecordsDecision_AndWritesTheMintedOrganizationBack()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D, A], LastUsedOrganizationId = D };
            var request = new TokenRequest { GrantType = GrantTypes.Password, OrganizationId = D };

            var refusal = await Sut().ResolveOrganizationAsync(user, request);

            refusal.Should().BeNull();
            request.OrganizationDecision.Should().NotBeNull();
            request.OrganizationDecision!.Scope.ClaimValue.Should().Be(A);
            request.OrganizationId.Should().Be(A);
        }

        [Fact]
        public async Task C1_Refused_Returns403_AndLeavesTheRequestUntouched()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D, A], LastUsedOrganizationId = A };
            var request = new TokenRequest { GrantType = GrantTypes.RefreshToken, OrganizationId = D, OrganizationSource = OrganizationRequestSource.Explicit };

            var refusal = await Sut().ResolveOrganizationAsync(user, request);

            refusal.Should().NotBeNull();
            refusal!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            refusal.Error.Should().Be("organization_disabled");
            refusal.ErrorDescription.Should().Be("Your organization is disabled. Please contact your administrator or support.");
            // C10: the body never names the organization.
            refusal.ErrorDescription.Should().NotContain(D);
            refusal.AccessToken.Should().BeNullOrEmpty();
            request.OrganizationDecision.Should().BeNull();
            request.OrganizationId.Should().Be(D);
        }

        [Fact]
        public async Task C4_StatusReadThrows_Returns503()
        {
            _resourceRepository.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());
            var request = new TokenRequest { GrantType = GrantTypes.Password };

            var refusal = await Sut().ResolveOrganizationAsync(new User { ItemId = "u1", OrganizationIds = [A] }, request);

            refusal!.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            refusal.Error.Should().Be("temporarily_unavailable");
            refusal.ErrorDescription.Should().Be("Organization status could not be checked. Please try again.");
            request.OrganizationDecision.Should().BeNull();
        }

        [Fact]
        public async Task ServerFilledSource_IsThePreference_LinkSource_RefusesDisabledLinkOrganization()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D, A] };

            var serverFilled = new TokenRequest { OrganizationId = D };
            (await Sut().ResolveOrganizationAsync(user, serverFilled)).Should().BeNull();
            serverFilled.OrganizationId.Should().Be(A);

            var link = new TokenRequest { GrantType = GrantTypes.SignupLink, OrganizationId = D, OrganizationSource = OrganizationRequestSource.Link };
            (await Sut().ResolveOrganizationAsync(user, link))!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        [Fact]
        public async Task C12_Impersonation_IsNotGated_EvenWhenTheOrganizationIsDisabled()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D] };
            var request = new TokenRequest { GrantType = GrantTypes.RefreshToken, IsImpersonation = true, OrganizationId = D, OrganizationSource = OrganizationRequestSource.Explicit };

            var refusal = await Sut().ResolveOrganizationAsync(user, request);

            refusal.Should().BeNull();
            request.OrganizationDecision!.Scope.ClaimValue.Should().Be(D);
            request.OrganizationDecision.OrganizationStatus.Should().BeNull();
            _resourceRepository.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        [Fact]
        public async Task C12_ImpersonationCloudGrant_IsNotGated()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D] };
            var request = new TokenRequest { GrantType = GrantTypes.ImpersonationCloud, OrganizationId = D };

            (await Sut().ResolveOrganizationAsync(user, request)).Should().BeNull();
            request.OrganizationDecision!.IsIssued.Should().BeTrue();
            request.OrganizationDecision.Scope.ClaimValue.Should().Be(D);
            _resourceRepository.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        [Fact]
        public async Task H12_MultiOrgOff_Default_NoStatusRead()
        {
            MultiOrg(allowNoOrg: true, enabled: false);
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var request = new TokenRequest { OrganizationId = D, OrganizationSource = OrganizationRequestSource.Explicit };

            (await Sut().ResolveOrganizationAsync(new User { ItemId = "u1", OrganizationIds = [D] }, request)).Should().BeNull();

            request.OrganizationId.Should().Be(IdpConstants.DefaultOrganizationId);
            _resourceRepository.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        // ---------- org_status claim ----------

        [Fact]
        public async Task H4_AllDisabled_SettingTrue_NoOrgToken_CarriesOrgStatus_AndNoRolesOrPermissions()
        {
            MultiOrg(allowNoOrg: true);
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = [D],
                Roles = new Dictionary<string, List<string>> { [D] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [D] = ["read"] }
            };
            var userRepository = new Mock<IUserRepository>();

            var token = await Sut(new AuthorizationClaimsResolver(userRepository.Object))
                .GetJwtAccessToken(Config(), MakeTenant(), user, new TokenRequest { GrantType = GrantTypes.Password });

            token.Claims.Should().ContainSingle(c => c.Type == BlocksContext.ORGANIZATION_ID_CLAIM).Which.Value.Should().Be("no-org");
            token.Claims.Should().ContainSingle(c => c.Type == JwtAccessTokenProvider.OrganizationStatusClaim).Which.Value.Should().Be("disabled");
            token.Claims.Should().NotContain(c => c.Type == BlocksContext.ROLES_CLAIM);
            token.Claims.Should().NotContain(c => c.Type == BlocksContext.PERMISSION_CLAIM);
        }

        [Fact]
        public async Task H4_NoOrgToken_ClaimsResolvedFromNoOrg()
        {
            MultiOrg(allowNoOrg: true);
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D] };

            await Sut().GetJwtAccessToken(Config(), MakeTenant(), user, new TokenRequest());

            _claimsResolver.Verify(r => r.ResolveAsync(user, IdpConstants.NoOrganizationId, It.IsAny<string?>(), It.IsAny<bool>()), Times.Once);
        }

        [Theory]
        [InlineData(A + "," + D)]
        [InlineData("")]
        public async Task OrgStatusClaim_AbsentOnEveryOtherToken(string membershipList)
        {
            MultiOrg(allowNoOrg: true);
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);

            var token = await Sut().GetJwtAccessToken(Config(), MakeTenant(), new User { ItemId = "u1", OrganizationIds = [.. membershipList.Split(',', StringSplitOptions.RemoveEmptyEntries)] }, new TokenRequest());

            token.Claims.Should().NotContain(c => c.Type == JwtAccessTokenProvider.OrganizationStatusClaim);
        }

        [Fact]
        public async Task GetJwtAccessToken_UsesThePriorDecision_WithoutASecondStatusRead()
        {
            _resourceRepository.WithActiveOrganizations();
            var user = new User { ItemId = "u1", OrganizationIds = [A] };
            var request = new TokenRequest();
            var sut = Sut();

            await sut.ResolveOrganizationAsync(user, request);
            var token = await sut.GetJwtAccessToken(Config(), MakeTenant(), user, request);

            token.Claims.Should().ContainSingle(c => c.Type == BlocksContext.ORGANIZATION_ID_CLAIM).Which.Value.Should().Be(A);
            _resourceRepository.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
        }

        [Fact]
        public async Task GetJwtAccessToken_RefusedWithoutAPriorDecision_ThrowsAndMintsNothing()
        {
            _resourceRepository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { ItemId = "u1", OrganizationIds = [D] };

            var act = () => Sut().GetJwtAccessToken(Config(), MakeTenant(), user, new TokenRequest());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*organization_disabled*");
            _claimsResolver.Verify(r => r.ResolveAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void OrganizationRefusal_MapsOutcomes()
        {
            JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.OrganizationDisabled).StatusCode.Should().Be(403);
            JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.StatusUnavailable).StatusCode.Should().Be(503);
        }
    }
}
