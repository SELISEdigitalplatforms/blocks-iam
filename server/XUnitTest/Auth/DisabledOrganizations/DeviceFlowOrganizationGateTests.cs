using System.Security.Cryptography;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 on the device flow: approval refused (403/503, request left pending, H3 active org stored, C3)
    /// and the device-code exchange re-check (C1/C6, Q2, H4 org_status).
    /// </summary>
    public sealed class DeviceFlowOrganizationGateTests
    {
        private const string TenantId = "tenant-1";
        private const string A = "org-a";
        private const string D = "org-d";
        private const string DeviceCode = "raw-device-code-1";

        private readonly Mock<IDeviceAuthorizationRepository> _repo = new();
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IOidcTokenMintService> _mint = new();
        private readonly DeviceCodeGenerator _generator = new();

        public DeviceFlowOrganizationGateTests()
        {
            MultiOrg(allowNoOrg: false);
            _mint.Setup(m => m.MintAsync(It.IsAny<OidcTokenMintRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OidcTokenMintResult { AccessToken = "at", IdToken = "it", RefreshToken = "rt", Scope = "openid", ExpiresIn = 600, AccessExpiry = DateTime.UtcNow, RefreshExpiry = DateTime.UtcNow.AddDays(1) });
        }

        private void MultiOrg(bool allowNoOrg) =>
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = allowNoOrg });

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        // ---------- approval ----------

        private DeviceVerificationService Verification() =>
            new(_repo.Object, _sessionRepo.Object, _authRepo.Object, _resourceRepo.Object, NullLogger<DeviceVerificationService>.Instance);

        private DeviceAuthorizationRequestModel ArrangeApproval(params string[] approverMemberships)
        {
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "req-1",
                UserCode = "ABCD-1234",
                ClientId = "client-1",
                TenantId = TenantId,
                RequestedScopes = "openid profile",
                Status = DeviceAuthorizationStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ApprovalTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("approval-token"))).ToLowerInvariant()
            };
            _repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _sessionRepo.Setup(s => s.GetBySessionIdAsync(It.IsAny<string>())).ReturnsAsync(new IdpSessionModel
            {
                SessionId = "session-1",
                IdleExpiry = DateTime.UtcNow.AddMinutes(10),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(1),
                Accounts = [new IdpSessionAccount { UserId = "approver-1", TenantId = TenantId, DisplayName = "User One" }]
            });
            _authRepo.Setup(r => r.GetUserByIdAsync("approver-1"))
                .ReturnsAsync(new User { ItemId = "approver-1", OrganizationIds = [.. approverMemberships] });
            _repo.Setup(r => r.MarkApprovedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
                .ReturnsAsync(true);
            return entity;
        }

        private Task<IActionResult> Allow()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");
            ctx.Request.Headers["Cookie"] = $"idp_session_id_{TenantId}=session-1";
            return Verification().DecisionAsync(
                new DeviceDecisionRequest { UserCode = "ABCD-1234", Decision = "allow", ApprovalToken = "approval-token" }, ctx);
        }

        private void VerifyStillPending()
        {
            _repo.Verify(r => r.MarkApprovedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
            _repo.Verify(r => r.MarkDeniedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task C3_Approve_AllDisabled_403_RequestNeitherApprovedNorDenied()
        {
            ArrangeApproval(D);
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);

            var result = await Allow();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(obj.Value, "error").Should().Be("organization_disabled");
            VerifyStillPending();
        }

        [Fact]
        public async Task C4_Approve_StatusReadThrows_503_StillPending()
        {
            ArrangeApproval(A);
            _resourceRepo.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());

            var result = await Allow();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            Prop(obj.Value, "error").Should().Be("temporarily_unavailable");
            VerifyStillPending();
        }

        [Fact]
        public async Task H3_Approve_MixedMemberships_StoresTheActiveOrganization()
        {
            ArrangeApproval(D, A);
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);

            var result = await Allow();

            result.Should().BeOfType<OkObjectResult>();
            _repo.Verify(r => r.MarkApprovedAsync("req-1", "approver-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), A), Times.Once);
        }

        [Fact]
        public async Task H4_Approve_AllDisabled_SettingTrue_StoresNoOrg()
        {
            MultiOrg(allowNoOrg: true);
            ArrangeApproval(D);
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);

            var result = await Allow();

            result.Should().BeOfType<OkObjectResult>();
            _repo.Verify(r => r.MarkApprovedAsync("req-1", "approver-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), IdpConstants.NoOrganizationId), Times.Once);
        }

        // ---------- device-code exchange ----------

        private DeviceCodeExchangeService Exchange() => new(
            _repo.Object, new DeviceCodeGenerator(), _mint.Object, _authRepo.Object,
            OidcClientSecretTestDoubles.Resolver(null), _userRepo.Object, Options.Create(new DeviceFlowOptions()),
            _resourceRepo.Object, NullLogger<DeviceCodeExchangeService>.Instance);

        private void ArrangeApproved(string? storedOrganization, params string[] memberships)
        {
            var hash = _generator.HashDeviceCode(DeviceCode);
            _repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Approved, UserId = "user-1", OrganizationId = storedOrganization,
                RequestedScopes = "openid offline_access", ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            _repo.Setup(r => r.MarkConsumedAsync("id1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _userRepo.Setup(u => u.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1", Email = "x@y.z", OrganizationIds = [.. memberships] });
            _authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(new OidcClientRegistration
            {
                ClientId = "c1", IsActive = true, IsDeviceFlowClient = true, ClientType = "public",
                TokenEndpointAuthMethod = "none", AllowedScopes = ["openid", "offline_access"]
            });
        }

        private Task<IActionResult> Poll()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["device_code"] = DeviceCode,
                ["client_id"] = "c1"
            });
            return Exchange().ExchangeAsync(ctx.Request);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task C6_C1_StoredOrganizationDisabledOrMissingSinceApproval_403_NoMint(bool missing)
        {
            ArrangeApproved(A, A, "org-b");
            if (missing) _resourceRepo.WithOrganizationStatuses(missing: [A]);
            else _resourceRepo.WithOrganizationStatuses(disabled: [A]);

            var result = await Poll();

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(obj.Value, "error").Should().Be("organization_disabled");
            _mint.Verify(m => m.MintAsync(It.IsAny<OidcTokenMintRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task C4_Exchange_StatusReadThrows_503()
        {
            ArrangeApproved(A, A);
            _resourceRepo.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new TimeoutException());

            var result = await Poll();

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
            _mint.Verify(m => m.MintAsync(It.IsAny<OidcTokenMintRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Exchange_StoredActive_MintedUnchanged_NoOrgStatus()
        {
            ArrangeApproved(A, A);
            _resourceRepo.WithActiveOrganizations();

            var result = await Poll();

            var dict = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            dict.Keys.Should().NotContain("org_status");
            _mint.Verify(m => m.MintAsync(It.Is<OidcTokenMintRequest>(r => r.OrganizationId == A && r.OrganizationStatus == null), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Q2_H4_Exchange_StoredNoOrg_StillAllDisabled_SettingTrue_OrgStatusInBodyAndMint()
        {
            MultiOrg(allowNoOrg: true);
            ArrangeApproved(IdpConstants.NoOrganizationId, D);
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);

            var result = await Poll();

            var dict = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            dict["org_status"].Should().Be("disabled");
            _mint.Verify(m => m.MintAsync(It.Is<OidcTokenMintRequest>(r => r.OrganizationId == IdpConstants.NoOrganizationId && r.OrganizationStatus == "disabled"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Q2_Exchange_StoredNoOrg_SettingTurnedOff_403()
        {
            ArrangeApproved(IdpConstants.NoOrganizationId, D);
            _resourceRepo.WithOrganizationStatuses(disabled: [D]);

            var result = await Poll();

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
            _mint.Verify(m => m.MintAsync(It.IsAny<OidcTokenMintRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
