using Authentication.DomainService.Utilities;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 §6.1 rules 1, 2 and 8, H11, H12, C4, C9 and the exchange re-check (C6, Q2):
    /// <see cref="OrganizationAccessResolver.ResolveForIssuanceAsync"/>, <see cref="OrganizationAccessResolver.ReadIssuanceSettingsAsync"/>,
    /// <see cref="OrganizationAccessResolver.RecheckStoredOrganizationAsync"/> and <see cref="OrganizationAccessResolver.MintedOrganizationId"/>.
    /// </summary>
    public class OrganizationAccessResolverIssuanceTests
    {
        private const string A = "org-a";
        private const string D = "org-d";
        private const string X = "org-x";

        private static readonly OrganizationIssuanceSettings MultiOrg = new(true, false);
        private static readonly OrganizationIssuanceSettings MultiOrgAllowNoOrg = new(true, true);

        private readonly Mock<IResourceRepository> _repository = new();

        private Task<OrganizationIssuanceDecision> Resolve(User user, string? requested = null, bool isExplicit = false, OrganizationIssuanceSettings? settings = null) =>
            OrganizationAccessResolver.ResolveForIssuanceAsync(
                _repository.Object, NullLogger.Instance, user, requested, isExplicit, isLinkOrganization: false, settings ?? MultiOrg);

        private Task<OrganizationIssuanceDecision> Recheck(User user, string? stored, OrganizationIssuanceSettings? settings = null) =>
            OrganizationAccessResolver.RecheckStoredOrganizationAsync(_repository.Object, NullLogger.Instance, user, stored, settings ?? MultiOrg);

        private void VerifyStatusReads(Times times) =>
            _repository.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), times);

        // ---------- H12 / C9 / §7 example 8 ----------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task H12_C9_MultiOrgOff_Default_WithZeroStatusReads_WhateverTheSetting(bool allowNoOrg)
        {
            _repository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { OrganizationIds = [D], LastUsedOrganizationId = D };

            var decision = await Resolve(user, requested: D, isExplicit: true, settings: new OrganizationIssuanceSettings(false, allowNoOrg));

            decision.IsIssued.Should().BeTrue();
            decision.Scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
            decision.OrganizationStatus.Should().BeNull();
            VerifyStatusReads(Times.Never());
        }

        // ---------- H11 ----------

        [Fact]
        public async Task H11_MultiOrgMint_WithCandidates_ReadsStatusExactlyOnce_ForAllCandidates()
        {
            _repository.WithOrganizationStatuses(disabled: [D]);
            var user = new User
            {
                OrganizationIds = [D, A],
                Roles = new Dictionary<string, List<string>> { [X] = ["admin"] },
                LastUsedOrganizationId = D
            };

            var decision = await Resolve(user, requested: D);

            decision.Scope.ClaimValue.Should().Be(A);
            VerifyStatusReads(Times.Once());
            _repository.Verify(r => r.GetOrganizationStatusesAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 3 && ids.Contains(D) && ids.Contains(A) && ids.Contains(X))), Times.Once);
        }

        [Fact]
        public async Task OnlySentinelMemberships_NoStatusRead()
        {
            _repository.WithActiveOrganizations();
            var user = new User { OrganizationIds = [IdpConstants.DefaultOrganizationId] };

            var decision = await Resolve(user, requested: IdpConstants.DefaultOrganizationId, isExplicit: true);

            decision.Scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
            VerifyStatusReads(Times.Never());
        }

        [Fact]
        public async Task H8_NoMemberships_NoStatusRead_PlainNoOrg()
        {
            _repository.WithActiveOrganizations();

            var decision = await Resolve(new User(), settings: MultiOrgAllowNoOrg);

            decision.Scope.ClaimValue.Should().Be(IdpConstants.NoOrganizationId);
            decision.OrganizationStatus.Should().BeNull();
            VerifyStatusReads(Times.Never());
        }

        // ---------- C4 ----------

        [Fact]
        public async Task C4_StatusReadThrows_StatusUnavailable()
        {
            _repository.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var decision = await Resolve(new User { OrganizationIds = [A] });

            decision.IsIssued.Should().BeFalse();
            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.StatusUnavailable);
        }

        [Fact]
        public async Task C3_AllDisabled_SettingFalse_Refused_SettingTrue_NoOrgDisabled()
        {
            _repository.WithOrganizationStatuses(disabled: [D], missing: [X]);
            var user = new User { OrganizationIds = [D, X] };

            (await Resolve(user)).Outcome.Should().Be(OrganizationIssuanceOutcome.OrganizationDisabled);

            var allowed = await Resolve(user, settings: MultiOrgAllowNoOrg);
            allowed.Scope.ClaimValue.Should().Be(IdpConstants.NoOrganizationId);
            allowed.OrganizationStatus.Should().Be("disabled");
        }

        // ---------- settings read ----------

        [Fact]
        public async Task ReadSettings_ReadsBothFlags()
        {
            _repository.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = true });

            var settings = await OrganizationAccessResolver.ReadIssuanceSettingsAsync(_repository.Object, NullLogger.Instance, "u1");

            settings.Should().Be(new OrganizationIssuanceSettings(true, true));
        }

        [Fact]
        public async Task ReadSettings_NoDocument_SingleOrganization()
        {
            _repository.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((IamTenantConfiguration)null!);

            var settings = await OrganizationAccessResolver.ReadIssuanceSettingsAsync(_repository.Object, NullLogger.Instance, "u1");

            settings.Should().Be(OrganizationIssuanceSettings.SingleOrganization);
        }

        [Fact]
        public async Task ReadSettings_Throws_TreatedAsMultiOrgOff_SoNoStatusIsRead()
        {
            _repository.Setup(r => r.GetTenantConfigurationAsync()).ThrowsAsync(new InvalidOperationException("down"));
            _repository.WithOrganizationStatuses(disabled: [D]);

            var settings = await OrganizationAccessResolver.ReadIssuanceSettingsAsync(_repository.Object, NullLogger.Instance, "u1");
            var decision = await Resolve(new User { OrganizationIds = [D] }, settings: settings);

            settings.IsMultiOrgEnabled.Should().BeFalse();
            settings.AllowNoOrgSignInForDisabledOrganizations.Should().BeFalse();
            decision.Scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
            VerifyStatusReads(Times.Never());
        }

        // ---------- exchange re-check (C6, Q2) ----------

        [Fact]
        public async Task Recheck_StoredActive_Kept()
        {
            _repository.WithActiveOrganizations();

            var decision = await Recheck(new User { OrganizationIds = [A] }, A);

            decision.IsIssued.Should().BeTrue();
            decision.Scope.ClaimValue.Should().Be(A);
            VerifyStatusReads(Times.Once());
        }

        [Theory]
        [InlineData(D)]
        [InlineData(X)]
        public async Task Recheck_C6_StoredDisabledOrMissing_Refused(string stored)
        {
            _repository.WithOrganizationStatuses(disabled: [D], missing: [X]);

            var decision = await Recheck(new User { OrganizationIds = [stored, A] }, stored);

            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        [Fact]
        public async Task Recheck_StoredNoOrg_IsRedecided_FromCurrentMemberships()
        {
            // At authorize every membership was disabled (no-org + disabled); A was enabled before exchange.
            _repository.WithActiveOrganizations();

            var decision = await Recheck(new User { OrganizationIds = [A] }, IdpConstants.NoOrganizationId, MultiOrgAllowNoOrg);

            decision.Scope.ClaimValue.Should().Be(A);
            decision.OrganizationStatus.Should().BeNull();
        }

        [Fact]
        public async Task Recheck_StoredNoOrg_StillAllDisabled_FollowsTheSetting()
        {
            _repository.WithOrganizationStatuses(disabled: [D]);
            var user = new User { OrganizationIds = [D] };

            (await Recheck(user, IdpConstants.NoOrganizationId, MultiOrgAllowNoOrg)).OrganizationStatus.Should().Be("disabled");
            (await Recheck(user, IdpConstants.NoOrganizationId, MultiOrg)).Outcome.Should().Be(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        [Fact]
        public async Task Recheck_StoredDefault_Kept_WithoutStatusRead()
        {
            _repository.WithActiveOrganizations();

            var decision = await Recheck(new User(), IdpConstants.DefaultOrganizationId);

            decision.Scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
            VerifyStatusReads(Times.Never());
        }

        [Fact]
        public async Task Recheck_MultiOrgOff_StoredKept_WithoutStatusRead()
        {
            _repository.WithOrganizationStatuses(disabled: [D]);

            var decision = await Recheck(new User { OrganizationIds = [D] }, D, OrganizationIssuanceSettings.SingleOrganization);

            decision.IsIssued.Should().BeTrue();
            decision.Scope.ClaimValue.Should().Be(D);
            VerifyStatusReads(Times.Never());
        }

        [Fact]
        public async Task Recheck_StatusReadThrows_StatusUnavailable()
        {
            _repository.Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()))
                .ThrowsAsync(new TimeoutException());

            var decision = await Recheck(new User { OrganizationIds = [A] }, A);

            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.StatusUnavailable);
        }

        // ---------- MintedOrganizationId ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(A)]
        [InlineData("default")]
        public void MintedOrganizationId_KeptDecision_ReturnsTheStoredValueItself(string? stored)
        {
            var decision = OrganizationScopeResolver.Issued(OrganizationScopeResolver.ForStoredOrganization(stored));

            OrganizationAccessResolver.MintedOrganizationId(stored, decision).Should().Be(stored);
        }

        [Fact]
        public void MintedOrganizationId_RedecidedScope_ReturnsTheNewClaimValue()
        {
            var redecided = OrganizationScopeResolver.ResolveIssuance(
                new User { OrganizationIds = [A] }, null, false, false,
                new Dictionary<string, bool> { [A] = false }, false, OrganizationAccessResolver.HasOrganizationAccess);

            OrganizationAccessResolver.MintedOrganizationId(IdpConstants.NoOrganizationId, redecided).Should().Be(A);
        }

        // ---------- review fixes: Q3 own memberships only, blank stored kept ----------

        [Fact]
        public async Task Q3_RequestedNonMember_IsNotLookedUp()
        {
            _repository.WithActiveOrganizations();
            var user = new User { OrganizationIds = [A] };

            var decision = await Resolve(user, requested: X, isExplicit: true);

            decision.Scope.ClaimValue.Should().Be(A);
            _repository.Verify(r => r.GetOrganizationStatusesAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains(A) && !ids.Contains(X))), Times.Once);
        }

        [Fact]
        public async Task Q10_LinkOrganization_IsLookedUp_EvenWhenNotAMembership()
        {
            _repository.WithOrganizationStatuses(disabled: [X]);
            var user = new User { OrganizationIds = [A] };

            var decision = await OrganizationAccessResolver.ResolveForIssuanceAsync(
                _repository.Object, NullLogger.Instance, user, X, isExplicit: true, isLinkOrganization: true, MultiOrg);

            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Recheck_StoredBlank_KeptAsStored_WithoutStatusRead(string? stored)
        {
            _repository.WithActiveOrganizations();
            var user = new User { OrganizationIds = [A] };

            var decision = await Recheck(user, stored);

            decision.IsIssued.Should().BeTrue();
            OrganizationAccessResolver.MintedOrganizationId(stored, decision).Should().Be(stored);
            VerifyStatusReads(Times.Never());
        }
    }
}
