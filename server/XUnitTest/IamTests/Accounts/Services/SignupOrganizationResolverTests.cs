using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Moq;

namespace XUnitTest.IamTests.Accounts.Services
{
    /// <summary>
    /// One row of the sign-up placement table per test, plus the resolver's own reads (C16): at most one
    /// organization read, and only for a real target id. The whole sign-up path reads more; that delta is
    /// pinned in AccountServiceTests.B1_C16_Signup_RealCreatePath_ReadsFourTimesForARealTarget_ZeroOtherwise.
    /// </summary>
    public class SignupOrganizationResolverTests
    {
        private const string TargetId = "org-sales";

        private readonly Mock<IResourceRepository> _repository = new(MockBehavior.Strict);

        private SignupOrganizationResolver Create() => new(_repository.Object);

        private static TenantConfiguration Config(
            bool multiOrg = true,
            bool autoJoin = false,
            bool creation = false,
            string? target = TargetId) => new()
            {
                IsMultiOrgEnabled = multiOrg,
                IsSignupAutoJoinEnabled = autoJoin,
                AllowOrgCreationFromSignup = creation,
                SignupAutoJoinOrganizationId = target
            };

        private void TargetIs(Organization? organization) =>
            _repository.Setup(r => r.GetOrganizationById(TargetId)).ReturnsAsync(organization!);

        private void VerifyOrganizationReads(int count) =>
            _repository.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Exactly(count));

        [Theory]
        [InlineData(SignupRoute.Email, false, false)]
        [InlineData(SignupRoute.Email, true, true)]
        [InlineData(SignupRoute.Sso, false, false)]
        [InlineData(SignupRoute.Sso, true, true)]
        public async Task C12_MultiOrgOff_IsDefault_WhateverAutoJoinIsStored(SignupRoute route, bool autoJoin, bool creation)
        {
            var decision = await Create().ResolveAsync(Config(multiOrg: false, autoJoin: autoJoin, creation: creation), route);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.Default, "default"));
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task C12_MissingConfiguration_IsDefault()
        {
            var decision = await Create().ResolveAsync(null, SignupRoute.Email);

            decision.Outcome.Should().Be(SignupOrganizationOutcome.Default);
            VerifyOrganizationReads(0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task H8_Email_AutoJoinOff_NoCreate_IsNoOrganization(bool creation)
        {
            var decision = await Create().ResolveAsync(Config(autoJoin: false, creation: creation), SignupRoute.Email);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.NoOrganization));
            VerifyOrganizationReads(0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task H7_Email_AutoJoinOn_CreateRequested_IsCreateOwn_AndNeverTheTarget(bool creation)
        {
            // With creation from sign-up off, CreateOrganizationAsync's creation gate then refuses the request.
            var decision = await Create().ResolveAsync(
                Config(autoJoin: true, creation: creation), SignupRoute.Email, createOrganizationRequested: true);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.CreateOwn));
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task Email_AutoJoinOff_CreateRequested_IsCreateOwn()
        {
            var decision = await Create().ResolveAsync(
                Config(autoJoin: false, creation: true), SignupRoute.Email, createOrganizationRequested: true);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.CreateOwn));
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task C12_Email_MultiOrgOff_CreateRequested_IsCreateOwn_SoTheMultiOrgGateRefusesItAsBefore()
        {
            // Not Default: a single-org sign-up that asks to create has always been refused by
            // CreateOrganizationAsync (multi_org_disabled), and keeping that path keeps C12's "exactly as today".
            var decision = await Create().ResolveAsync(
                Config(multiOrg: false, autoJoin: true, creation: true), SignupRoute.Email, createOrganizationRequested: true);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.CreateOwn));
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task Sso_AutoJoinOff_CreationOff_IsNoOrganization()
        {
            var decision = await Create().ResolveAsync(Config(autoJoin: false, creation: false), SignupRoute.Sso);

            decision.Outcome.Should().Be(SignupOrganizationOutcome.NoOrganization);
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task Sso_AutoJoinOff_CreationOn_IsCreatePersonal()
        {
            var decision = await Create().ResolveAsync(Config(autoJoin: false, creation: true), SignupRoute.Sso);

            decision.Outcome.Should().Be(SignupOrganizationOutcome.CreatePersonal);
            VerifyOrganizationReads(0);
        }

        [Theory]
        [InlineData(SignupRoute.Email, false)]
        [InlineData(SignupRoute.Email, true)]
        [InlineData(SignupRoute.Sso, false)]
        [InlineData(SignupRoute.Sso, true)]
        public async Task H5_H9_H11_AutoJoinOn_EnabledTarget_IsJoinTarget_WithOneRead(SignupRoute route, bool creation)
        {
            TargetIs(new Organization { ItemId = TargetId, Name = "Sales" });

            var decision = await Create().ResolveAsync(Config(autoJoin: true, creation: creation), route);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.JoinTarget, TargetId));
            VerifyOrganizationReads(1);
        }

        [Theory]
        [InlineData(SignupRoute.Email)]
        [InlineData(SignupRoute.Sso)]
        public async Task C16_H5_H9_AutoJoinOn_DefaultTarget_IsJoinTarget_WithoutAnyRead(SignupRoute route)
        {
            var decision = await Create().ResolveAsync(Config(autoJoin: true, target: "default"), route);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.JoinTarget, "default"));
            VerifyOrganizationReads(0);
        }

        [Theory]
        [InlineData(SignupRoute.Email)]
        [InlineData(SignupRoute.Sso)]
        public async Task C6_C8_AutoJoinOn_MissingTarget_IsTargetUnavailable(SignupRoute route)
        {
            TargetIs(null);

            var decision = await Create().ResolveAsync(Config(autoJoin: true), route);

            decision.Should().Be(new SignupOrganizationDecision(SignupOrganizationOutcome.TargetUnavailable));
            VerifyOrganizationReads(1);
        }

        [Theory]
        [InlineData(SignupRoute.Email)]
        [InlineData(SignupRoute.Sso)]
        public async Task C6_C8_AutoJoinOn_DisabledTarget_IsTargetUnavailable(SignupRoute route)
        {
            TargetIs(new Organization { ItemId = TargetId, Name = "Sales", IsDisabled = true });

            var decision = await Create().ResolveAsync(Config(autoJoin: true), route);

            decision.Outcome.Should().Be(SignupOrganizationOutcome.TargetUnavailable);
            decision.OrganizationId.Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task AutoJoinOn_BlankStoredTarget_IsTargetUnavailable_WithoutAnyRead(string? target)
        {
            var decision = await Create().ResolveAsync(Config(autoJoin: true, target: target), SignupRoute.Email);

            decision.Outcome.Should().Be(SignupOrganizationOutcome.TargetUnavailable);
            VerifyOrganizationReads(0);
        }

        [Fact]
        public async Task C16_AutoJoinOff_NeverReadsTheStoredTarget()
        {
            await Create().ResolveAsync(Config(autoJoin: false, target: TargetId), SignupRoute.Email);
            await Create().ResolveAsync(Config(autoJoin: false, creation: true, target: TargetId), SignupRoute.Sso);

            VerifyOrganizationReads(0);
        }
    }
}
