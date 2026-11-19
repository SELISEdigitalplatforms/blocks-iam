using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Iam.DomainService.Notifications;
using Iam.DomainService.Users;
using Moq;
using XUnitTest.TestSupport;
using static XUnitTest.IamTests.Batch.BatchHarness;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// The worker side of the batch upsert (spec 683 §6.5): bulk preload, in-memory validation, one bulk
    /// write, side effects, the job's final state and the one-running-job rule.
    /// </summary>
    public class BatchUserServiceProcessTests : IDisposable
    {
        private readonly BatchHarness _h = new();

        public BatchUserServiceProcessTests()
        {
            // The worker's context is whatever the message carried; the service must not depend on it.
            AccessGrantTestKit.SignOut();
        }

        public void Dispose()
        {
            AccessGrantTestKit.SignOut();
            GC.SuppressFinalize(this);
        }

        private BatchJob Job => _h.Jobs.Jobs["job-1"];

        private (List<User> Inserts, List<UserBulkUpdate> Updates) SingleWrite() => _h.BulkWrites.Should().ContainSingle().Subject;

        #region Happy path

        [Fact]
        public async Task NewUser_TwoOrganizations_IsCreatedPendingWithRolesPerOrganization_H2_H8()
        {
            var item = Item(0, "a@example.com", Membership(OrgA, ["viewer"]), Membership(OrgB, ["editor"]));
            item.Data.SendEmail = true;
            await _h.ProcessAsync(_h.QueueJob("job-1", item));

            var user = SingleWrite().Inserts.Should().ContainSingle().Subject;
            user.Email.Should().Be("a@example.com");
            user.ProvisioningSource.Should().Be(UserProvisioningSource.API);
            user.UserCreationType.Should().Be(UserCreationType.Api);
            user.UserPassType.Should().Be(UserPassType.Password);
            user.Password.Should().BeEmpty();
            user.Status.Should().Be(UserLifecycleStatus.PendingVerification);
            user.Active.Should().BeFalse();
            user.OrganizationIds.Should().Equal(OrgA, OrgB);
            user.Roles[OrgA].Should().Equal("viewer");
            user.Roles[OrgB].Should().Equal("editor");
            user.Tags.Should().BeEmpty();
            user.CreatedBy.Should().Be(Caller);

            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(user, OrgA, true, Caller), Times.Once);
            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Summary.Created.Should().Be(1);
            Job.Items.Should().BeNull();
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData(null, true, true)]
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]
        public async Task NewUser_SendMail_ResolvesItemThenOptions_H8(bool? itemSendEmail, bool optionSendEmail, bool expected)
        {
            var item = Item(0, "a@example.com", Membership(OrgA, ["viewer"]));
            item.Data.SendEmail = itemSendEmail;
            var job = _h.QueueJob("job-1", item);
            job.Options.SendEmail = optionSendEmail;

            await _h.ProcessAsync(job);

            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(It.IsAny<User>(), OrgA, expected, Caller), Times.Once);
        }

        [Fact]
        public async Task ExistingUser_NewOrganization_IsAddedAndOtherOrganizationsUntouched_H3_H9()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var item = Item(0, "b@example.com", Membership(OrgA, ["viewer"]), Membership(OrgB, ["editor"]));
            item.Data.SendEmail = true;

            await _h.ProcessAsync(_h.QueueJob("job-1", item));

            var update = SingleWrite().Updates.Should().ContainSingle().Subject;
            update.UserId.Should().Be("u1");
            var rendered = Render(update.Update).ToString();
            rendered.Should().Contain("\"$addToSet\" : { \"OrganizationIds\" : \"org-b\" }");
            rendered.Should().Contain("\"Roles.org-b\" : [\"editor\"]");
            rendered.Should().NotContain("Roles.org-a");
            rendered.Should().NotContain("$pull").And.NotContain("$unset");

            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.Is<User>(u => u.ItemId == "u1"), OrgB), Times.Once);
            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.IsAny<User>(), OrgA), Times.Never);
            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task ExistingUser_NewOrganization_WithoutSendEmail_SendsNoMail()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgB, ["editor"]))));

            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task ExistingMember_ExtraRole_IsMergedAsUnion_H4_Example2()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA, ["editor"]))));

            var rendered = Render(SingleWrite().Updates.Single().Update).ToString();
            rendered.Should().Contain("\"$addToSet\" : { \"Roles.org-a\" : \"editor\" }");
            Job.Summary.Updated.Should().Be(1);
            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
            _h.SentNotifications.Should().NotContain(n => n.ConfigurationName == BuiltInNotificationNames.UserRolesChanged);
        }

        [Fact]
        public async Task ItemThatChangesNothing_IsUnchanged_WithNoWriteEventOrMail_H10()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer", "editor"] }, new() { [OrgA] = ["docs.read"] });
            var item = Item(0, "b@example.com", Membership(OrgA, ["editor"], ["docs.read"]));
            item.Data.FirstName = "First";
            item.Data.SendEmail = true;

            await _h.ProcessAsync(_h.QueueJob("job-1", item));

            _h.BulkWrites.Should().BeEmpty();
            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
            Job.Summary.Unchanged.Should().Be(1);
            Job.Status.Should().Be(BatchJobStatus.Completed);
        }

        [Fact]
        public async Task NewMembershipWithoutRoles_GetsTheOrganizationDefaultRoles_H5()
        {
            _h.WithOrganization(OrgB, false, "viewer");
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });

            await _h.ProcessAsync(_h.QueueJob("job-1",
                Item(0, "new@example.com", Membership(OrgB)),
                Item(1, "b@example.com", Membership(OrgB, []))));

            var write = SingleWrite();
            write.Inserts.Single().Roles[OrgB].Should().Equal("viewer");
            Render(write.Updates.Single().Update).ToString().Should().Contain("\"Roles.org-b\" : [\"viewer\"]");
        }

        [Fact]
        public async Task ExistingMembershipWithoutRoles_ChangesNothing()
        {
            _h.WithOrganization(OrgA, false, "editor");
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA))));

            _h.BulkWrites.Should().BeEmpty();
            Job.Summary.Unchanged.Should().Be(1);
        }

        [Fact]
        public async Task ProfileFieldDifferent_IsSet_WhenCallerHoldsUpdateUserProfiles_H6()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var item = Item(0, "b@example.com");
            item.Data.FirstName = "Renamed";
            item.Data.LastName = "Last";
            item.Data.Attributes = new Dictionary<string, object> { ["legacyUserId"] = "123" };

            await _h.ProcessAsync(_h.QueueJob("job-1", item));

            var rendered = Render(SingleWrite().Updates.Single().Update).ToString();
            rendered.Should().Contain("\"FirstName\" : \"Renamed\"");
            rendered.Should().NotContain("\"LastName\"");
            rendered.Should().Contain("\"legacyUserId\" : \"123\"");
            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task DefaultOrganizationCaller_MayUseAnyOrganization_H7()
        {
            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["admin"]), Membership(OrgB, ["viewer"]))));

            Job.Summary.Created.Should().Be(1);
            Job.Failures.Should().BeEmpty();
        }

        [Fact]
        public async Task Finish_WritesSummaryFailuresAndOneNotification_H11_H12()
        {
            var job = _h.QueueJob("job-1",
                Item(1, "a@example.com", Membership("org-x")),
                Item(2, "b@example.com", Membership(OrgA, ["viewer"])),
                Item(3, "c@example.com", Membership(OrgA, ["viewer"])));
            job.Total = 4;
            job.Failures = [new BatchItemFailure { Ref = "ref-0", Index = 0, Email = "bad", Errors = [BatchErrorCodes.Error("data.email", BatchErrorCodes.EmailInvalid)] }];
            job.Summary.Failed = 1;

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            Job.Summary.Should().BeEquivalentTo(new BatchJobSummary { Created = 2, Updated = 0, Unchanged = 0, Failed = 2 });
            Job.Failures.Select(f => f.Index).Should().Equal(0, 1);
            Job.Failures[1].Errors.Should().ContainSingle(e => e.Code == BatchErrorCodes.OrganizationNotFound && e.Field == "data.memberships[0].organizationId");
            Job.Items.Should().BeNull();
            Job.CompletedAt.Should().NotBeNull();

            var notification = _h.SentNotifications.Should().ContainSingle().Subject;
            notification.ConfigurationName.Should().Be(BuiltInNotificationNames.BatchJobFinished);
            notification.UserIds.Should().Equal(Caller);
            notification.TenantId.Should().Be(BatchHarness.Tenant);
            var payload = (Dictionary<string, object?>)notification.Payload;
            payload["title"].Should().Be("Batch import finished");
            payload["description"].Should().Be("2 created, 0 updated, 0 unchanged, 2 failed.");
            payload["isSuccess"].Should().Be(false);
            payload["jobId"].Should().Be("job-1");
            payload["status"].Should().Be(BatchJobStatus.CompletedWithErrors);
            payload["total"].Should().Be(4);
        }

        [Fact]
        public async Task ClientSubmitter_GetsNoNotification()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            job.RequestedBy = null;
            job.RequestedBySystemCaller = true;

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.Completed);
            _h.SentNotifications.Should().BeEmpty();
        }

        [Fact]
        public async Task Job_ReadsInBulk_AndWritesOnce_H14()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var items = Enumerable.Range(0, 20)
                .Select(i => Item(i, $"u{i}@example.com", Membership(OrgA, ["viewer"]), Membership(OrgB, ["editor"]), Membership("default", ["base"])))
                .Append(Item(20, "b@example.com", Membership(OrgB, ["viewer"])))
                .ToArray();

            await _h.ProcessAsync(_h.QueueJob("job-1", items));

            _h.Users.Verify(u => u.GetUsersByEmailsAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
            _h.Resources.Verify(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>()), Times.Once);
            _h.Resources.Verify(r => r.GetRolesBySlugAndOrgAsync(It.IsAny<List<string>>(), OrgA), Times.Once);
            _h.Resources.Verify(r => r.GetRolesBySlugAndOrgAsync(It.IsAny<List<string>>(), OrgB), Times.Once);
            _h.Resources.Verify(r => r.GetRolesBySlugAndOrgAsync(It.IsAny<List<string>>(), "default"), Times.Once);
            _h.Users.Verify(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()), Times.Once);
            _h.Users.Verify(u => u.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
            _h.Users.Verify(u => u.CreateUserAsync(It.IsAny<User>()), Times.Never);
            _h.Users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            Job.Summary.Created.Should().Be(20);
            Job.Summary.Updated.Should().Be(1);
        }

        #endregion

        #region Critical path

        [Theory]
        [InlineData("org-x", null, "data.memberships[0].organizationId", BatchErrorCodes.OrganizationNotFound)]
        [InlineData("org-off", null, "data.memberships[0].organizationId", BatchErrorCodes.OrganizationDisabled)]
        [InlineData(OrgA, "ghost", "data.memberships[0].roles[0]", BatchErrorCodes.RoleNotFound)]
        [InlineData(OrgA, "archived", "data.memberships[0].roles[0]", BatchErrorCodes.RoleNotFound)]
        [InlineData("", null, "data.memberships[0].organizationId", BatchErrorCodes.OrganizationRequired)]
        public async Task BadMembership_FailsTheWholeUser_C5(string organizationId, string? role, string field, string code)
        {
            _h.WithOrganization("org-off", disabled: true);
            _h.Roles.Add(new Role { ItemId = "r-arch", Slug = "archived", Name = "archived", OrganizationId = OrgA, IsArchived = true });
            var item = Item(0, "a@example.com", Membership(organizationId, role is null ? null : [role]));

            await _h.ProcessAsync(_h.QueueJob("job-1", item, Item(1, "ok@example.com", Membership(OrgA, ["viewer"]))));

            var failure = Job.Failures.Should().ContainSingle().Subject;
            failure.Index.Should().Be(0);
            failure.Email.Should().Be("a@example.com");
            failure.Errors.Should().ContainSingle(e => e.Field == field && e.Code == code);
            SingleWrite().Inserts.Should().ContainSingle(u => u.Email == "ok@example.com");
            Job.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
        }

        [Fact]
        public async Task MultiOrgOff_NonDefaultOrganization_IsNotSupported_AndDefaultIsStoredAsDefault_C5()
        {
            _h.MultiOrg = false;

            await _h.ProcessAsync(_h.QueueJob("job-1",
                Item(0, "a@example.com", Membership(OrgA, ["viewer"])),
                Item(1, "b@example.com", Membership(null, ["base"])),
                Item(2, "c@example.com"),
                Item(3, "d@example.com", Membership("default", ["e2e-batch-role"]))));

            Job.Failures.Should().ContainSingle().Which.Errors.Should().ContainSingle(e => e.Code == BatchErrorCodes.OrganizationNotSupported);
            var inserts = SingleWrite().Inserts;
            inserts.Should().HaveCount(3).And.OnlyContain(u => u.OrganizationIds.SequenceEqual(new[] { "default" }));
            inserts.Single(u => u.Email == "b@example.com").Roles["default"].Should().Equal("base");
            inserts.Single(u => u.Email == "c@example.com").Roles["default"].Should().BeEmpty();
            _h.Resources.Verify(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact]
        public async Task MultiOrgOn_NewUserWithoutMemberships_FailsWithOrganizationRequired()
        {
            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com")));

            Job.Failures.Single().Errors.Should().ContainSingle(e => e.Field == "data.memberships" && e.Code == BatchErrorCodes.OrganizationRequired);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task OrganizationCaller_OtherOrganization_IsNotAllowed_FromTheJobNotTheContext_C6()
        {
            // The worker's context claims the default organization; the job says the caller was in org-a.
            AccessGrantTestKit.SignIn("someone-else", "default");
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgB, ["viewer"])), Item(1, "b@example.com", Membership(OrgA, ["viewer"])));
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            var failure = Job.Failures.Should().ContainSingle().Subject;
            failure.Index.Should().Be(0);
            failure.Errors.Should().ContainSingle(e => e.Code == BatchErrorCodes.OrganizationNotAllowed && e.Message == "Other org user can not add/update");
            SingleWrite().Inserts.Should().ContainSingle(u => u.Email == "b@example.com");
        }

        [Fact]
        public async Task OrganizationCaller_RoleAboveItself_IsNotGrantable_C7()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithUser("manager-1", ["manager"], organizationId: OrgA);
            var job = _h.QueueJob("job-1",
                Item(0, "a@example.com", Membership(OrgA, ["admin"])),
                Item(1, "b@example.com", Membership(OrgA, ["viewer"])),
                Item(2, "c@example.com", Membership(OrgA, null, ["docs.read"])));
            job.RequestedBy = "manager-1";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Select(f => f.Index).Should().Equal(0, 2);
            Job.Failures.Should().OnlyContain(f => f.Errors.Single().Code == BatchErrorCodes.GrantNotAllowed);
            SingleWrite().Inserts.Should().ContainSingle(u => u.Email == "b@example.com");
            _h.Kit.Logged("R1-AssignRole").Should().BeTrue();
        }

        [Fact]
        public async Task OrganizationCaller_NewUserAccessAttributesOfAnotherOrganization_AreRefused_R10()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            var item = Item(0, "a@example.com", Membership(OrgA, ["viewer"]));
            item.Data.Attributes = new Dictionary<string, object>
            {
                ["access"] = new Dictionary<string, object> { [OrgB] = new Dictionary<string, object> { ["region"] = new List<object> { "eu" } } }
            };
            var job = _h.QueueJob("job-1", item);
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.GrantNotAllowed);
        }

        [Fact]
        public async Task OrganizationCaller_ExistingUserAboveItself_IsNotGrantable_R4_R3()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithUser("manager-1", ["manager"], organizationId: OrgA);
            _h.Kit.WithPermission(UserPermissions.ManageUserRoles, OrgA, "manager");
            _h.WithStoredUser("boss", "boss@example.com", new() { [OrgA] = ["admin"] });
            var job = _h.QueueJob("job-1", Item(0, "boss@example.com", Membership(OrgA, ["viewer"])));
            job.RequestedBy = "manager-1";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.GrantNotAllowed);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task OrganizationCaller_AddsToMember_NeedsManageUserRoles_C8()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA, ["manager"])));
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Should().ContainSingle(e => e.Field == "data" && e.Code == BatchErrorCodes.ManageUserRolesRequired);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task OrganizationCaller_WithManageUserRoles_AddsToMember()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithPermission(UserPermissions.ManageUserRoles, OrgA, "admin");
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA, ["manager"])));
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Summary.Updated.Should().Be(1);
            Job.Failures.Should().BeEmpty();
        }

        [Fact]
        public async Task DefaultOrganizationCaller_WithoutManageUserRoles_CannotAddToMember_Q1B_C8()
        {
            _h.Kit.WithUser("limited", ["importer"], organizationId: "default");
            _h.WithStoredUser("u1", "b@example.com", new() { ["default"] = ["base"] });
            var job = _h.QueueJob("job-1",
                Item(0, "b@example.com", Membership("default", ["e2e-batch-role"])),
                Item(1, "new@example.com", Membership("default", ["e2e-batch-role"])));
            job.RequestedBy = "limited";

            await _h.ProcessAsync(job);

            var failure = Job.Failures.Should().ContainSingle().Subject;
            failure.Index.Should().Be(0);
            failure.Errors.Should().ContainSingle(e => e.Field == "data" && e.Code == BatchErrorCodes.ManageUserRolesRequired);
            SingleWrite().Inserts.Should().ContainSingle(u => u.Email == "new@example.com");
            SingleWrite().Updates.Should().BeEmpty();
        }

        [Fact]
        public async Task DefaultOrganizationCaller_WithoutUpdateUserProfiles_CannotChangeAProfile_Q1B_C9()
        {
            _h.Kit.WithUser("limited", ["importer"], organizationId: "default");
            _h.WithStoredUser("u1", "b@example.com", new() { ["default"] = ["base"] });
            var changed = Item(0, "b@example.com");
            changed.Data.FirstName = "Renamed";
            var job = _h.QueueJob("job-1", changed);
            job.RequestedBy = "limited";

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Should().ContainSingle(e => e.Field == "data" && e.Code == BatchErrorCodes.UpdateUserProfilesRequired);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task DefaultOrganizationCaller_WithoutUpdateUserProfiles_SameValuesNeedNoCheck()
        {
            _h.Kit.WithUser("limited", ["importer"], organizationId: "default");
            _h.WithStoredUser("u1", "b@example.com", new() { ["default"] = ["base"] });
            var same = Item(0, "b@example.com");
            same.Data.FirstName = "First";
            var job = _h.QueueJob("job-1", same);
            job.RequestedBy = "limited";

            await _h.ProcessAsync(job);

            Job.Summary.Unchanged.Should().Be(1);
            Job.Failures.Should().BeEmpty();
        }

        [Fact]
        public async Task DefaultOrganizationCaller_WithoutManageUserRoles_MayStillAddANewOrganization()
        {
            _h.Kit.WithUser("limited", ["importer"], organizationId: "default");
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgB, ["viewer"])));
            job.RequestedBy = "limited";

            await _h.ProcessAsync(job);

            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task DefaultOrganizationClient_IsCheckedAgainstItsOwnPermissions_Q1B()
        {
            _h.Kit.WithClient("importer-client", ["importer"], organizationId: "default");
            _h.Kit.WithClient("admin-client", ["tenant-admin"], organizationId: "default");
            _h.WithStoredUser("u1", "b@example.com", new() { ["default"] = ["base"] });

            var limited = _h.QueueJob("job-1", Item(0, "b@example.com", Membership("default", ["e2e-batch-role"])));
            limited.RequestedBy = null;
            limited.RequestedByClientId = "importer-client";
            await _h.ProcessAsync(limited);
            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.ManageUserRolesRequired);

            var allowed = _h.QueueJob("job-2", Item(0, "b@example.com", Membership("default", ["e2e-batch-role"])));
            allowed.RequestedBy = null;
            allowed.RequestedByClientId = "admin-client";
            await _h.ProcessAsync(allowed);
            _h.Jobs.Jobs["job-2"].Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task PlatformCall_WithNoUserAndNoClient_SkipsThePerUserChecks_Q1B()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { ["default"] = ["base"] });
            var item = Item(0, "b@example.com", Membership("default", ["e2e-batch-role"]));
            item.Data.FirstName = "Renamed";
            var job = _h.QueueJob("job-1", item);
            job.RequestedBy = null;

            await _h.ProcessAsync(job);

            Job.Summary.Updated.Should().Be(1);
            _h.Kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task MergedPermissionsOverFive_FailTheUser_C10()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] }, new() { [OrgA] = ["p1", "p2", "p3"] });

            await _h.ProcessAsync(_h.QueueJob("job-1",
                Item(0, "b@example.com", Membership(OrgA, null, ["p3", "p4", "p5", "p6"]))));

            Job.Failures.Single().Errors.Should().ContainSingle(e => e.Field == "data.memberships[0].permissions" && e.Code == BatchErrorCodes.TooManyPermissions);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task ShorterOrEmptyLists_RemoveNothing_C11()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer", "editor"] }, new() { [OrgA] = ["docs.read"] });

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA, ["viewer"], []))));

            _h.BulkWrites.Should().BeEmpty();
            Job.Summary.Unchanged.Should().Be(1);
        }

        [Fact]
        public async Task OrganizationCaller_OwnAccount_IsNotAllowed_C19()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithPermission(UserPermissions.ManageUserRoles, OrgA, "admin");
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            _h.WithStoredUser("org-admin", "me@example.com", new() { [OrgA] = ["admin"] });
            var job = _h.QueueJob("job-1", Item(0, "me@example.com", Membership(OrgA, ["viewer"])));
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.GrantNotAllowed);
        }

        [Fact]
        public async Task OrganizationCaller_ProfileOfUserOutsideItsOrganization_IsNotGrantable()
        {
            _h.Kit.WithStandardTree(OrgA);
            _h.Kit.WithPermission(UserPermissions.UpdateUserProfiles, OrgA, "admin");
            _h.Kit.WithUser("org-admin", ["admin"], organizationId: OrgA);
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgB] = ["viewer"] });
            var item = Item(0, "b@example.com");
            item.Data.FirstName = "Renamed";
            var job = _h.QueueJob("job-1", item);
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = OrgA;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.GrantNotAllowed);
        }

        [Fact]
        public async Task DeactivatedAccount_StaysDeactivated_C21()
        {
            var user = _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            user.Active = false;
            user.Status = UserLifecycleStatus.Disabled;

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgB, ["viewer"]))));

            var rendered = Render(SingleWrite().Updates.Single().Update).ToString();
            rendered.Should().NotContain("\"Status\"").And.NotContain("\"Active\"");
            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task FailedWrite_FailsOnlyThatUser_AndSendsNoneOfItsSideEffects_C15()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1",
                Item(0, "dup@example.com", Membership(OrgA, ["viewer"])),
                Item(1, "ok@example.com", Membership(OrgA, ["viewer"])),
                Item(2, "b@example.com", Membership(OrgB, ["viewer"])));
            job.Options.SendEmail = true;
            _h.Users.Setup(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()))
                .ReturnsAsync((IReadOnlyList<User> inserts, IReadOnlyList<UserBulkUpdate> _) => new UserBulkWriteResult(
                    new HashSet<string> { inserts.Single(u => u.Email == "dup@example.com").ItemId },
                    new HashSet<string> { "u1" }));

            await _h.ProcessAsync(job);

            Job.Failures.Select(f => (f.Index, f.Errors.Single().Code)).Should().Equal(
                (0, BatchErrorCodes.EmailConflict), (2, BatchErrorCodes.WriteFailed));
            Job.Summary.Should().BeEquivalentTo(new BatchJobSummary { Created = 1, Failed = 2 });
            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(It.Is<User>(u => u.Email == "ok@example.com"), OrgA, true, Caller), Times.Once);
            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(It.Is<User>(u => u.Email == "dup@example.com"), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
            _h.Mutations.Verify(m => m.NotifyOrganizationMemberAddedAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SideEffects_ArePersistedBeforeTheWrite_AndClearedAfter()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            List<BatchPendingSideEffect>? pendingAtWrite = null;
            _h.Users.Setup(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()))
                .Callback(() => pendingAtWrite = _h.Jobs.Jobs["job-1"].PendingSideEffects.ToList())
                .ReturnsAsync(_h.WriteResult);

            await _h.ProcessAsync(job);

            pendingAtWrite.Should().ContainSingle(s => s.Kind == BatchSideEffectKinds.Created && s.OrganizationId == OrgA);
            _h.Jobs.PendingWrites.Last().Should().BeEmpty();
        }

        [Fact]
        public async Task SideEffectFailure_NeverFailsTheJob()
        {
            _h.Mutations.Setup(m => m.PublishBatchUserCreatedAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("bus down"));

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"]))));

            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Summary.Created.Should().Be(1);
        }

        [Fact]
        public async Task ResumedJob_ReplaysPendingSideEffects_AndCreatesNoDuplicate_C13()
        {
            // The first attempt wrote the user and died before clearing its side effects and finishing.
            var written = _h.WithStoredUser("u-new", "a@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            job.Status = BatchJobStatus.Running;
            job.Attempt = 1;
            job.HeartbeatAt = DateTime.UtcNow.AddMinutes(-5);
            job.PendingSideEffects = [new BatchPendingSideEffect { UserId = "u-new", Kind = BatchSideEffectKinds.Created, OrganizationId = OrgA, SendEmail = true }];

            await _h.ProcessAsync(job);

            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(written, OrgA, true, Caller), Times.Once);
            _h.BulkWrites.Should().BeEmpty();
            Job.Attempt.Should().Be(2);
            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Summary.Unchanged.Should().Be(1);
            Job.PendingSideEffects.Should().BeEmpty();
            _h.SentNotifications.Should().ContainSingle();
        }

        [Fact]
        public async Task RedeliveredMessageOfAFinishedJob_DoesNothing_C13()
        {
            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"]))));

            await _h.ProcessAsync(Job);

            _h.BulkWrites.Should().ContainSingle();
            _h.SentNotifications.Should().ContainSingle();
            _h.Mutations.Verify(m => m.PublishBatchUserCreatedAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);
        }

        [Fact]
        public async Task LiveRunningJob_BlocksTheTenant_ForABoundedWait_C14()
        {
            var running = _h.QueueJob("job-0", DateTime.UtcNow.AddMinutes(-1));
            running.Status = BatchJobStatus.Running;
            running.HeartbeatAt = _h.Now.AddSeconds(-10);
            _h.OnDelay = () => running.HeartbeatAt = _h.Now;
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            var started = _h.Now;

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.Queued);
            _h.BulkWrites.Should().BeEmpty();
            running.Status.Should().Be(BatchJobStatus.Running);
            (_h.Now - started).Should().BeGreaterThanOrEqualTo(BatchUserService.StaleAfter).And.BeLessThan(TimeSpan.FromSeconds(90));
        }

        [Fact]
        public async Task JobWaitingBehindARunningJob_StartsWhenThatJobFinishes_C14()
        {
            var running = _h.QueueJob("job-0", DateTime.UtcNow.AddMinutes(-1));
            running.Status = BatchJobStatus.Running;
            running.HeartbeatAt = _h.Now;
            _h.OnDelay = () =>
            {
                running.HeartbeatAt = _h.Now;
                if (_h.Delays == 2)
                {
                    running.Status = BatchJobStatus.Completed;
                }
            };

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"]))));

            Job.Status.Should().Be(BatchJobStatus.Completed);
            _h.Delays.Should().Be(2);
        }

        [Fact]
        public async Task RedeliveryWhileItsWorkerIsSilent_TakesTheJobOverOnceTheHeartbeatIsStale_C13()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            job.Status = BatchJobStatus.Running;
            job.Attempt = 1;
            job.HeartbeatAt = _h.Now.AddSeconds(-5);

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Attempt.Should().Be(2);
            _h.Delays.Should().BeGreaterThan(0);
            SingleWrite().Inserts.Should().ContainSingle();
        }

        [Fact]
        public async Task RedeliveryWhileItsWorkerIsAlive_LeavesTheJobToThatWorker()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            job.Status = BatchJobStatus.Running;
            job.Attempt = 1;
            job.HeartbeatAt = _h.Now;
            _h.OnDelay = () =>
            {
                job.HeartbeatAt = _h.Now;
                if (_h.Delays == 3)
                {
                    job.Status = BatchJobStatus.Completed;
                }
            };

            await _h.ProcessAsync(job);

            Job.Attempt.Should().Be(1);
            _h.Delays.Should().Be(3);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task WorkerWhoseJobWasTakenOver_CannotFinishOrClearItsSuccessorsJob()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            _h.Users.Setup(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()))
                .Callback(() =>
                {
                    // Another worker took the job over while this one was writing: the stored job is now its attempt.
                    _h.Jobs.Jobs["job-1"] = new BatchJob
                    {
                        ItemId = "job-1",
                        TenantId = job.TenantId,
                        Status = BatchJobStatus.Running,
                        Attempt = job.Attempt + 1,
                        HeartbeatAt = _h.Now,
                        Items = job.Items,
                        PendingSideEffects = [new BatchPendingSideEffect { UserId = "theirs", Kind = BatchSideEffectKinds.Created }]
                    };
                })
                .ReturnsAsync(_h.WriteResult);

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.Running);
            Job.PendingSideEffects.Should().ContainSingle(s => s.UserId == "theirs");
            _h.SentNotifications.Should().BeEmpty();
        }

        [Fact]
        public async Task RunningJob_DrainsTheQueuedJobsOfTheTenant_InCreationOrder_C14()
        {
            var first = _h.QueueJob("job-1", DateTime.UtcNow.AddSeconds(-30), Item(0, "a@example.com", Membership(OrgA, ["viewer"])));
            _h.QueueJob("job-3", DateTime.UtcNow.AddSeconds(-10), Item(0, "c@example.com", Membership(OrgA, ["viewer"])));
            _h.QueueJob("job-2", DateTime.UtcNow.AddSeconds(-20), Item(0, "b@example.com", Membership(OrgA, ["viewer"])));

            await _h.ProcessAsync(first);

            _h.Jobs.Jobs.Values.Should().OnlyContain(j => j.Status == BatchJobStatus.Completed);
            _h.BulkWrites.Select(w => w.Inserts.Single().Email).Should().Equal("a@example.com", "b@example.com", "c@example.com");
        }

        [Fact]
        public async Task StaleRunningJob_IsRequeued_AndThisJobStarts()
        {
            var stale = _h.QueueJob("job-0", DateTime.UtcNow.AddMinutes(-10), Item(0, "z@example.com", Membership(OrgA, ["viewer"])));
            stale.Status = BatchJobStatus.Running;
            stale.HeartbeatAt = DateTime.UtcNow.AddMinutes(-2);
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.Completed);
            stale.Status.Should().Be(BatchJobStatus.Completed, "the requeued job is drained after this one");
        }

        [Fact]
        public async Task OneItemThatThrows_FailsOnlyThatUser()
        {
            _h.Mutations.Setup(m => m.MapUser(It.Is<CreateUserRequest>(r => r.Email == "boom@example.com")))
                .Throws(new InvalidOperationException("boom@example.com is broken"));

            await _h.ProcessAsync(_h.QueueJob("job-1",
                Item(0, "boom@example.com", Membership(OrgA, ["viewer"])),
                Item(1, "ok@example.com", Membership(OrgA, ["viewer"]))));

            Job.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            Job.Failures.Single().Errors.Single().Code.Should().Be(BatchErrorCodes.WriteFailed);
            Job.Summary.Created.Should().Be(1);
            string.Join("\n", _h.Logger.Messages).Should().NotContain("boom@example.com").And.Contain("InvalidOperationException");
        }

        [Fact]
        public async Task StoredUserWithNullMembershipFields_IsUpdatedWhole()
        {
            var user = _h.WithStoredUser("u1", "b@example.com");
            user.OrganizationIds = null!;
            user.Roles = null!;
            user.Permissions = null!;

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "b@example.com", Membership(OrgA, ["viewer"]))));

            var rendered = Render(SingleWrite().Updates.Single().Update).ToString();
            rendered.Should().Contain("\"OrganizationIds\" : [\"org-a\"]");
            rendered.Should().Contain("\"Roles\" : { \"org-a\" : [\"viewer\"] }");
            Job.Summary.Updated.Should().Be(1);
        }

        [Fact]
        public async Task TwoMembershipsResolvingToTheDefaultOrganization_AreADuplicate()
        {
            _h.MultiOrg = false;

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(null, ["base"]), Membership("default", ["e2e-batch-role"]))));

            Job.Failures.Single().Errors.Should().ContainSingle(e => e.Field == "data.memberships[1].organizationId" && e.Code == BatchErrorCodes.OrganizationDuplicate);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task SameAttributesInAnotherOrder_AreUnchanged()
        {
            var user = _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            user.Attributes = new Dictionary<string, object> { ["a"] = "1", ["b"] = new Dictionary<string, object> { ["y"] = "2", ["x"] = "3" } };
            _h.Kit.WithUser("limited", ["importer"], organizationId: "default");
            var item = Item(0, "b@example.com");
            item.Data.Attributes = new Dictionary<string, object> { ["b"] = new Dictionary<string, object> { ["x"] = "3", ["y"] = "2" }, ["a"] = "1" };
            var job = _h.QueueJob("job-1", item);
            job.RequestedBy = "limited";

            await _h.ProcessAsync(job);

            Job.Summary.Unchanged.Should().Be(1);
            Job.Failures.Should().BeEmpty();
        }

        [Fact]
        public async Task OrganizationCallerWithoutRecordedOrganization_FailsClosed()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])), Item(1, "b@example.com", Membership("default")));
            job.RequestedBy = "org-admin";
            job.RequestedByOrganizationId = null;
            job.RequestedBySystemCaller = false;

            await _h.ProcessAsync(job);

            Job.Failures.Should().HaveCount(2).And.OnlyContain(f => f.Errors.Single().Code == BatchErrorCodes.OrganizationNotAllowed);
            _h.BulkWrites.Should().BeEmpty();
        }

        [Fact]
        public async Task FailedWrite_IsDroppedFromThePendingSideEffects_BeforeAnyIsSent()
        {
            _h.WithStoredUser("u1", "b@example.com", new() { [OrgA] = ["viewer"] });
            var job = _h.QueueJob("job-1", Item(0, "ok@example.com", Membership(OrgA, ["viewer"])), Item(1, "b@example.com", Membership(OrgB, ["viewer"])));
            job.Options.SendEmail = true;
            _h.WriteResult = new UserBulkWriteResult(new HashSet<string>(), new HashSet<string> { "u1" });

            await _h.ProcessAsync(job);

            _h.Jobs.PendingWrites[0].Should().HaveCount(2);
            _h.Jobs.PendingWrites[1].Should().ContainSingle(s => s.Kind == BatchSideEffectKinds.Created);
            _h.Jobs.PendingWrites[2].Should().BeEmpty();
        }

        [Fact]
        public async Task ErrorAfterTheWrite_FinishesWithTheWriteResult_NotAsFailed()
        {
            _h.Jobs.FailNextClear = new InvalidOperationException("db blip");

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"]))));

            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Summary.Created.Should().Be(1);
            Job.Error.Should().BeNull();
        }

        [Fact]
        public async Task SideEffectPhase_SendsHeartbeats()
        {
            var items = Enumerable.Range(0, 25).Select(i => Item(i, $"u{i}@example.com", Membership(OrgA, ["viewer"]))).ToArray();

            await _h.ProcessAsync(_h.QueueJob("job-1", items));

            _h.Jobs.Heartbeats.Should().BeGreaterThanOrEqualTo(3);
            Job.Summary.Created.Should().Be(25);
        }

        [Fact]
        public async Task TenantMismatch_FailsTheJob_AndTouchesNoUser_C17()
        {
            var job = _h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"])));

            await _h.Service().ProcessAsync(new BatchJobQueuedEvent { JobId = "job-1", TenantId = "other-tenant" });

            job.Status.Should().Be(BatchJobStatus.Failed);
            job.Error.Should().Be(BatchErrorCodes.TenantMismatch);
            _h.Users.Verify(u => u.GetUsersByEmailsAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
            _h.Users.Verify(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()), Times.Never);
        }

        [Fact]
        public async Task MissingJob_IsLoggedAndIgnored()
        {
            await _h.Service().ProcessAsync(new BatchJobQueuedEvent { JobId = "nope", TenantId = BatchHarness.Tenant });

            _h.Logger.Messages.Should().Contain(m => m.Contains("nope", StringComparison.Ordinal));
            _h.Users.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UnexpectedError_FailsTheJobWithProcessingFailed_AndNotifiesOnce()
        {
            _h.Users.Setup(u => u.GetUsersByEmailsAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new InvalidOperationException("db down"));

            await _h.ProcessAsync(_h.QueueJob("job-1", Item(0, "a@example.com", Membership(OrgA, ["viewer"]))));

            Job.Status.Should().Be(BatchJobStatus.Failed);
            Job.Error.Should().Be(BatchErrorCodes.ProcessingFailed);
            Job.Items.Should().BeNull();
            var payload = (Dictionary<string, object?>)_h.SentNotifications.Single().Payload;
            payload["isSuccess"].Should().Be(false);
            payload["status"].Should().Be(BatchJobStatus.Failed);
        }

        [Fact]
        public async Task Logs_CarryNoEmailNameOrPhone_C20()
        {
            _h.WithStoredUser("u1", "existing.person@example.com", new() { [OrgA] = ["viewer"] });
            var created = Item(0, "secret.person@example.com", Membership(OrgA, ["viewer"]));
            created.Data.FirstName = "Zebediah";
            created.Data.PhoneNumber = "+8801711111111";
            var failing = Item(1, "failing.person@example.com", Membership("org-x"));
            var updated = Item(2, "existing.person@example.com", Membership(OrgB, ["viewer"]));
            updated.Data.LastName = "Quixote";
            _h.Mutations.Setup(m => m.PublishBatchUserCreatedAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("bus down"));

            await _h.ProcessAsync(_h.QueueJob("job-1", created, failing, updated));

            _h.Logger.Messages.Should().NotBeEmpty();
            var all = string.Join("\n", _h.Logger.Messages);
            all.Should().NotContain("@").And.NotContain("Zebediah").And.NotContain("Quixote").And.NotContain("8801711111111");
            all.Should().Contain("job-1");
        }

        #endregion
    }
}
