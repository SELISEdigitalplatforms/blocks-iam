using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Utilities;

namespace XUnitTest.IamTests.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 §6.1 / §7: <see cref="OrganizationScopeResolver.ResolveIssuance"/> and
    /// <see cref="OrganizationScopeResolver.CollectStatusCandidates"/>. Multi-org is on in every case here
    /// (multi-org off never reaches this resolver). "A" is active, "D" disabled, "X" has no document.
    /// </summary>
    public class OrganizationScopeResolverIssuanceTests
    {
        private const string A = "org-a";
        private const string B = "org-b";
        private const string D = "org-d";
        private const string X = "org-x";

        // Same membership test as OrganizationAccessResolver.HasOrganizationAccess (kept local to this assembly's view).
        private static bool HasAccess(User user, string? organizationId) =>
            !string.IsNullOrWhiteSpace(organizationId)
            && (user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId));

        // X is deliberately absent: a missing document.
        private static readonly IReadOnlyDictionary<string, bool> Statuses =
            new Dictionary<string, bool>(StringComparer.Ordinal) { [A] = false, [B] = false, [D] = true };

        private static OrganizationIssuanceDecision Resolve(
            User user,
            string? requested = null,
            bool isExplicit = false,
            bool isLink = false,
            bool allowNoOrg = false,
            IReadOnlyDictionary<string, bool>? statuses = null) =>
            OrganizationScopeResolver.ResolveIssuance(user, requested, isExplicit, isLink, statuses ?? Statuses, allowNoOrg, HasAccess);

        private static void ShouldIssue(OrganizationIssuanceDecision decision, string organizationId, string? status = null)
        {
            decision.IsIssued.Should().BeTrue();
            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.Issue);
            decision.Scope.ClaimValue.Should().Be(organizationId);
            decision.OrganizationStatus.Should().Be(status);
        }

        private static void ShouldRefuseDisabled(OrganizationIssuanceDecision decision)
        {
            decision.IsIssued.Should().BeFalse();
            decision.Outcome.Should().Be(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        // ---------- §7 examples ----------

        [Fact]
        public void Example1_MembershipsDA_LastUsedD_IssuesA_WithoutStatus()
        {
            var user = new User { OrganizationIds = [D, A], LastUsedOrganizationId = D };

            ShouldIssue(Resolve(user, requested: D), A);
        }

        [Fact]
        public void Example2_RefreshHintD_UserAlsoInA_Refused()
        {
            var user = new User { OrganizationIds = [D, A], LastUsedOrganizationId = A };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: true));
        }

        [Fact]
        public void Example2_FollowingPlainLogin_LandsInA()
        {
            var user = new User { OrganizationIds = [D, A], LastUsedOrganizationId = D };

            ShouldIssue(Resolve(user), A);
        }

        [Fact]
        public void Example3_SwitchTargetD_Refused()
        {
            var user = new User { OrganizationIds = [A, D], LastUsedOrganizationId = A };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: true));
        }

        [Theory]
        [InlineData(D)]
        [InlineData(X)]
        [InlineData(D + "," + X)]
        public void Example4_AllDisabledOrMissing_SettingFalse_Refused(string membershipList)
        {
            var memberships = membershipList.Split(',');
            var user = new User { OrganizationIds = [.. memberships], LastUsedOrganizationId = memberships[0] };

            ShouldRefuseDisabled(Resolve(user, allowNoOrg: false));
        }

        [Theory]
        [InlineData(D)]
        [InlineData(X)]
        [InlineData(D + "," + X)]
        public void Example4_H4_AllDisabledOrMissing_SettingTrue_NoOrgWithDisabledStatus(string membershipList)
        {
            var memberships = membershipList.Split(',');
            var user = new User { OrganizationIds = [.. memberships], LastUsedOrganizationId = memberships[0] };

            var decision = Resolve(user, allowNoOrg: true);

            ShouldIssue(decision, IdpConstants.NoOrganizationId, OrganizationScopeResolver.DisabledOrganizationStatus);
            decision.Scope.Kind.Should().Be(OrganizationScopeKind.None);
            decision.OrganizationStatus.Should().Be("disabled");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Example5_H8_NoMembershipIds_NoOrgWithoutStatus_WhateverTheSetting(bool allowNoOrg)
        {
            ShouldIssue(Resolve(new User(), allowNoOrg: allowNoOrg), IdpConstants.NoOrganizationId);
        }

        [Fact]
        public void Example6_TokenExchangeGrantD_Refused_GrantA_Issued()
        {
            var user = new User { OrganizationIds = [A, D] };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: true));
            ShouldIssue(Resolve(user, requested: A, isExplicit: true), A);
        }

        [Fact]
        public void Example7_AuthorizeMembershipsD_SettingFalse_Refused()
        {
            ShouldRefuseDisabled(Resolve(new User { OrganizationIds = [D] }));
        }

        // ---------- H1 / H2 / H7 ----------

        [Fact]
        public void H1_NoOrgSent_LastUsedActive_IssuesLastUsed()
        {
            var user = new User { OrganizationIds = [A, B, D], LastUsedOrganizationId = B };

            ShouldIssue(Resolve(user), B);
        }

        [Fact]
        public void H1_NoOrgSent_LastUsedDisabled_IssuesFirstActiveMembership()
        {
            var user = new User { OrganizationIds = [D, B, A], LastUsedOrganizationId = D };

            ShouldIssue(Resolve(user), B);
        }

        [Fact]
        public void H1_FirstActive_FollowsOrganizationIdsThenRolesThenPermissions()
        {
            var user = new User
            {
                OrganizationIds = [D],
                Roles = new Dictionary<string, List<string>> { [X] = ["admin"], [B] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [A] = ["read"] }
            };

            ShouldIssue(Resolve(user), B);
        }

        [Fact]
        public void H1_OnlyAPermissionsMembershipActive_IssuesIt()
        {
            var user = new User
            {
                OrganizationIds = [D],
                Roles = new Dictionary<string, List<string>> { [X] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [A] = ["read"] }
            };

            ShouldIssue(Resolve(user), A);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void H2_RequestedActiveMembership_IssuedExactlyAsBefore(bool isExplicit)
        {
            var user = new User { OrganizationIds = [A, B], LastUsedOrganizationId = A };

            ShouldIssue(Resolve(user, requested: B, isExplicit: isExplicit), B);
        }

        [Fact]
        public void H7_ReEnabledOrganization_IsIssuedAgain_WithoutCleanup()
        {
            var user = new User { OrganizationIds = [D, A], LastUsedOrganizationId = D };
            var reEnabled = new Dictionary<string, bool>(StringComparer.Ordinal) { [A] = false, [D] = false };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: true));
            ShouldIssue(Resolve(user, requested: D, isExplicit: true, statuses: reEnabled), D);
            ShouldIssue(Resolve(user, statuses: reEnabled), D);
        }

        // ---------- C1: explicit organization is that organization or a refusal ----------

        [Theory]
        [InlineData(D)]
        [InlineData(X)]
        public void C1_ExplicitMemberDisabledOrMissing_Refused_NoFallback(string requested)
        {
            // An active A and an active last-used are available; neither is used.
            var user = new User { OrganizationIds = [requested, A], LastUsedOrganizationId = A };

            ShouldRefuseDisabled(Resolve(user, requested: requested, isExplicit: true));
        }

        [Fact]
        public void C1_ExplicitNonMember_IsIgnored_AsBefore()
        {
            var user = new User { OrganizationIds = [A, B], LastUsedOrganizationId = B };

            ShouldIssue(Resolve(user, requested: D, isExplicit: true), B);
        }

        [Fact]
        public void C1_ExplicitMembershipThroughRoleKey_Disabled_Refused()
        {
            var user = new User
            {
                OrganizationIds = [A],
                Roles = new Dictionary<string, List<string>> { [D] = ["admin"] }
            };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: true));
        }

        // ---------- server-filled preference ----------

        [Theory]
        [InlineData(D)]
        [InlineData(X)]
        public void ServerFilledPreference_DisabledOrMissing_FallsBackToLastUsed(string preference)
        {
            var user = new User { OrganizationIds = [preference, A, B], LastUsedOrganizationId = B };

            ShouldIssue(Resolve(user, requested: preference, isExplicit: false), B);
        }

        [Fact]
        public void ServerFilledPreference_DisabledAndLastUsedDisabled_FallsBackToFirstActive()
        {
            var user = new User { OrganizationIds = [D, X, A], LastUsedOrganizationId = D };

            ShouldIssue(Resolve(user, requested: D, isExplicit: false), A);
        }

        [Fact]
        public void ServerFilledPreference_AllDisabled_FollowsTheSetting()
        {
            var user = new User { OrganizationIds = [D] };

            ShouldRefuseDisabled(Resolve(user, requested: D, isExplicit: false, allowNoOrg: false));
            ShouldIssue(Resolve(user, requested: D, isExplicit: false, allowNoOrg: true), IdpConstants.NoOrganizationId, "disabled");
        }

        // ---------- C11: sentinels ----------

        [Fact]
        public void C11_ExplicitDefault_HeldByTheUser_IsActiveWithoutAStatusEntry()
        {
            var user = new User { OrganizationIds = [A, IdpConstants.DefaultOrganizationId], LastUsedOrganizationId = A };

            var decision = Resolve(user, requested: IdpConstants.DefaultOrganizationId, isExplicit: true,
                statuses: new Dictionary<string, bool>());

            ShouldIssue(decision, IdpConstants.DefaultOrganizationId);
            decision.Scope.Kind.Should().Be(OrganizationScopeKind.TenantWide);
        }

        [Fact]
        public void C11_DefaultMembership_IsActive_WhenEveryOtherMembershipIsDisabled()
        {
            var user = new User { OrganizationIds = [D, IdpConstants.DefaultOrganizationId], LastUsedOrganizationId = D };

            ShouldIssue(Resolve(user), IdpConstants.DefaultOrganizationId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void C11_ExplicitNoOrg_IsNeverHonoured(bool isExplicit)
        {
            var user = new User { OrganizationIds = [A, IdpConstants.NoOrganizationId] };

            ShouldIssue(Resolve(user, requested: IdpConstants.NoOrganizationId, isExplicit: isExplicit), A);
        }

        [Fact]
        public void C11_ExplicitNoOrg_ForUserWithoutMemberships_KeepsPlainNoOrg()
        {
            ShouldIssue(Resolve(new User(), requested: IdpConstants.NoOrganizationId, isExplicit: true), IdpConstants.NoOrganizationId);
        }

        // ---------- links ----------

        [Theory]
        [InlineData(D, true)]
        [InlineData(D, false)]
        [InlineData(X, true)]
        [InlineData(X, false)]
        public void Link_DisabledOrMissing_Refused_MemberOrNot(string linkOrganization, bool isMember)
        {
            var user = new User { OrganizationIds = isMember ? [linkOrganization, A] : [A], LastUsedOrganizationId = A };

            ShouldRefuseDisabled(Resolve(user, requested: linkOrganization, isExplicit: true, isLink: true));
        }

        [Fact]
        public void Link_ActiveNonMember_ResolvesAsBefore()
        {
            var user = new User { OrganizationIds = [B], LastUsedOrganizationId = B };

            ShouldIssue(Resolve(user, requested: A, isExplicit: true, isLink: true), B);
        }

        [Fact]
        public void Link_ActiveMember_IssuesTheLinkOrganization()
        {
            var user = new User { OrganizationIds = [A, B], LastUsedOrganizationId = B };

            ShouldIssue(Resolve(user, requested: A, isExplicit: true, isLink: true), A);
        }

        // ---------- candidate collection ----------

        [Fact]
        public void Candidates_RequestedThenLastUsedThenOrgIdsRolesPermissions_Deduplicated()
        {
            var user = new User
            {
                LastUsedOrganizationId = B,
                OrganizationIds = [A, B],
                Roles = new Dictionary<string, List<string>> { [D] = ["admin"], [A] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [X] = ["read"], [D] = ["read"] }
            };

            OrganizationScopeResolver.CollectStatusCandidates(user, "org-r")
                .Should().Equal("org-r", B, A, D, X);
        }

        [Fact]
        public void Candidates_ExcludeSentinelsAndBlanks()
        {
            var user = new User
            {
                LastUsedOrganizationId = IdpConstants.NoOrganizationId,
                OrganizationIds = [IdpConstants.DefaultOrganizationId, "", "  ", A],
                Roles = new Dictionary<string, List<string>> { [IdpConstants.NoOrganizationId] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [IdpConstants.DefaultOrganizationId] = ["read"] }
            };

            OrganizationScopeResolver.CollectStatusCandidates(user, IdpConstants.DefaultOrganizationId)
                .Should().Equal(A);
        }

        [Fact]
        public void Candidates_OnlySentinels_IsEmpty()
        {
            var user = new User { OrganizationIds = [IdpConstants.DefaultOrganizationId], LastUsedOrganizationId = IdpConstants.DefaultOrganizationId };

            OrganizationScopeResolver.CollectStatusCandidates(user, IdpConstants.NoOrganizationId).Should().BeEmpty();
            OrganizationScopeResolver.CollectStatusCandidates(new User(), null).Should().BeEmpty();
        }

        // ---------- helpers ----------

        [Theory]
        [InlineData(null, "no-org")]
        [InlineData("", "no-org")]
        [InlineData("no-org", "no-org")]
        [InlineData("default", "default")]
        [InlineData(A, A)]
        public void ForStoredOrganization_MapsTheStoredValue(string? stored, string expected)
        {
            OrganizationScopeResolver.ForStoredOrganization(stored).ClaimValue.Should().Be(expected);
        }

        [Fact]
        public void Refused_NeverCarriesAStatus()
        {
            var refused = OrganizationScopeResolver.Refused(OrganizationIssuanceOutcome.StatusUnavailable);

            refused.IsIssued.Should().BeFalse();
            refused.OrganizationStatus.Should().BeNull();
        }
    }
}
