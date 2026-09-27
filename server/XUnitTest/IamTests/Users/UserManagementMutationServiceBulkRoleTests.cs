using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Bulk role change -- the preview endpoint, the submit endpoint, the apply loop the worker
    /// drives, and the <see cref="UserManagementMutationService.ApplyRoleDelta"/> primitive all three
    /// share.
    /// <para>
    /// The recurring theme in these tests is that the preview is the <em>only</em> safety gate: the
    /// write is handed to a background worker that reports nothing back, so a count the operator
    /// approved has to be the count that is written. That is why so much here asserts on equality
    /// between what preview says and what submit queues, and on nothing at all being written in the
    /// request path.
    /// </para>
    /// </summary>
    public class UserManagementMutationServiceBulkRoleTests : IDisposable
    {
        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IValidator<UpdateMyAccountRequest>> _myAccountValidator = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly List<ConsumerMessage<BulkUserRoleChangeEvent>> _published = new();

        private const string Org = "org_acme";

        public UserManagementMutationServiceBulkRoleTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();

            _resourceRepo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync(new Organization { ItemId = Org, Name = "Acme" });

            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BulkUserRoleChangeEvent>>()))
                .Callback<ConsumerMessage<BulkUserRoleChangeEvent>>(m => _published.Add(m))
                .Returns(Task.CompletedTask);
        }

        private static void InstallContext(string orgId = "default")
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
            GC.SuppressFinalize(this);
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object,
                _myAccountValidator.Object, _iam.Object, _userRepo.Object, _message.Object, _cache.Object,
                _tenants.Object, _activity.Object, null, _resourceRepo.Object, null);

        /// <summary>Make the resolution path return this exact set, with this reported total.</summary>
        private void Matches(IEnumerable<User> users, long? totalCount = null)
        {
            var list = users.ToList();
            _userRepo.Setup(r => r.GetUsersAsync<User, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((list.AsQueryable(), totalCount ?? list.Count));
        }

        private static User UserWith(string id, params string[] roles) =>
            new()
            {
                ItemId = id,
                OrganizationIds = [Org],
                Roles = new Dictionary<string, List<string>> { [Org] = roles.ToList() }
            };

        private static BulkRoleChangeRequest Add(string role, params string[] userIds) =>
            new()
            {
                OrganizationId = Org,
                AddRoles = [role],
                Target = new BulkRoleTarget { UserIds = userIds.ToList() }
            };

        private static BulkRoleChangeRequest ByFilter(GetUsersFilter filter, string[]? add = null, string[]? remove = null) =>
            new()
            {
                OrganizationId = Org,
                AddRoles = (add ?? []).ToList(),
                RemoveRoles = (remove ?? []).ToList(),
                Target = new BulkRoleTarget { Filter = filter }
            };

        // ---------- ApplyRoleDelta (the primitive both sides share) ----------

        [Fact]
        public void ApplyRoleDelta_AppendsInOrderAndReportsTheChange()
        {
            var user = UserWith("u1", "member", "editor");

            var changed = UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], []);

            changed.Should().BeTrue();
            user.Roles[Org].Should().Equal("member", "editor", "viewer");
        }

        [Fact]
        public void ApplyRoleDelta_AlreadyHeld_IsANoOpAndReportsNoChange()
        {
            var user = UserWith("u1", "member", "viewer");

            var changed = UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], []);

            changed.Should().BeFalse();
            user.Roles[Org].Should().Equal("member", "viewer");
        }

        [Fact]
        public void ApplyRoleDelta_RemoveThenAdd_RemovalWinsOverTheExistingEntryOrder()
        {
            var user = UserWith("u1", "member", "editor", "viewer");

            var changed = UserManagementMutationService.ApplyRoleDelta(user, Org, ["auditor"], ["editor"]);

            changed.Should().BeTrue();
            user.Roles[Org].Should().Equal("member", "viewer", "auditor");
        }

        [Fact]
        public void ApplyRoleDelta_RemovingTheOnlyRole_LeavesAnEmptyListNotAMissingKey()
        {
            var user = UserWith("u9", "editor");

            var changed = UserManagementMutationService.ApplyRoleDelta(user, Org, [], ["editor"]);

            changed.Should().BeTrue();
            user.Roles[Org].Should().BeEmpty();
            user.OrganizationIds.Should().Contain(Org);
        }

        [Fact]
        public void ApplyRoleDelta_RemovingARoleTheUserDoesNotHold_IsANoOp()
        {
            var user = UserWith("u1", "member");

            UserManagementMutationService.ApplyRoleDelta(user, Org, [], ["nonexistent"]).Should().BeFalse();
        }

        [Fact]
        public void ApplyRoleDelta_UserWithNineRoles_GetsATenth()
        {
            // C5/A4 -- no per-user role cap exists anywhere in this repo, so nothing here may
            // reject, skip or truncate. A regression that reintroduced a cap would fail here first.
            var user = UserWith("u12", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9");

            UserManagementMutationService.ApplyRoleDelta(user, Org, ["r10"], []).Should().BeTrue();

            user.Roles[Org].Should().HaveCount(10).And.Contain("r10");
        }

        [Fact]
        public void ApplyRoleDelta_UnknownOrganization_AddsTheMembershipAlongWithTheRole()
        {
            var user = new User { ItemId = "u1" };

            UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], []).Should().BeTrue();

            user.Roles[Org].Should().Equal("viewer");
            user.OrganizationIds.Should().Contain(Org);
        }

        [Fact]
        public void ApplyRoleDelta_LeavesOtherOrganizationsUntouched()
        {
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = [Org, "org_northwind"],
                Roles = new Dictionary<string, List<string>>
                {
                    [Org] = ["member"],
                    ["org_northwind"] = ["admin"]
                }
            };

            UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], ["member"]);

            user.Roles["org_northwind"].Should().Equal("admin");
        }

        [Fact]
        public void ApplyRoleDelta_AppliedTwice_ConvergesWithoutASecondChange()
        {
            // C8 -- this is what makes a redelivered queue message safe.
            var user = UserWith("u1", "member");

            UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], ["member"]).Should().BeTrue();
            UserManagementMutationService.ApplyRoleDelta(user, Org, ["viewer"], ["member"]).Should().BeFalse();
            user.Roles[Org].Should().Equal("viewer");
        }

        // ---------- Preview: happy path ----------

        [Fact]
        public async Task Preview_FilterTarget_SplitsMatchedIntoAffectedAndUnchanged_AndWritesNothing()
        {
            // H1, H2, H5, H7, C9
            var users = Enumerable.Range(0, 309).Select(i => UserWith($"u{i}", "member")).ToList();
            users.AddRange(Enumerable.Range(0, 3).Select(i => UserWith($"v{i}", "member", "viewer")));
            Matches(users);

            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Roles = ["member"] }, add: ["viewer"]));

            result.IsSuccess.Should().BeTrue();
            result.MatchedCount.Should().Be(312);
            result.AffectedCount.Should().Be(309);
            result.UnchangedCount.Should().Be(3);
            (result.AffectedCount + result.UnchangedCount).Should().Be(result.MatchedCount);
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BulkUserRoleChangeEvent>>()), Times.Never);
        }

        [Fact]
        public async Task Preview_ScopesResolutionToTheOneRequestedOrganization()
        {
            // H2/A2 -- the organization the caller named is what pins the scope, not anything in the
            // filter body, so a filter can never widen the blast radius past what was authorised.
            UserListScope? seen = null;
            _userRepo.Setup(r => r.GetUsersAsync<User, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .Callback<GetUsersRequest, UserListScope>((_, s) => seen = s)
                .ReturnsAsync((Enumerable.Empty<User>().AsQueryable(), 0L));

            await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { OrganizationIds = ["org_somewhere_else"] }, add: ["viewer"]));

            seen!.Kind.Should().Be(UserListScopeKind.Organizations);
            seen.OrganizationIds.Should().Equal(Org);
        }

        [Fact]
        public async Task Preview_IdTarget_CountsOnlyTheIdsInsideTheOrganization()
        {
            // H3 / example 11 -- an id belonging to another organization is silently not matched,
            // because resolution runs through the same organization-scoped query the list uses.
            Matches([UserWith("u1", "member"), UserWith("u2", "member"), UserWith("u3", "member")]);

            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1", "u2", "u3", "u_other"));

            result.MatchedCount.Should().Be(3);
            result.AffectedCount.Should().Be(3);
        }

        [Fact]
        public async Task Preview_WholeSetIsANoOp_IsStillASuccess()
        {
            // H5/A6 -- "add Viewer to everyone matching" is legitimate even when everyone has it.
            Matches([UserWith("u1", "viewer"), UserWith("u2", "viewer")]);

            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1", "u2"));

            result.IsSuccess.Should().BeTrue();
            result.AffectedCount.Should().Be(0);
            result.UnchangedCount.Should().Be(2);
        }

        [Fact]
        public async Task Preview_RemovingARoleNobodyHolds_IsASuccessfulNoOpNotAnError()
        {
            // C10
            Matches([UserWith("u1", "member"), UserWith("u2", "member"), UserWith("u3", "member")]);

            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Roles = ["member"] }, remove: ["nonexistent"]));

            result.IsSuccess.Should().BeTrue();
            result.AffectedCount.Should().Be(0);
            result.UnchangedCount.Should().Be(3);
        }

        [Fact]
        public async Task Preview_NoUserWithAnyNumberOfRolesIsSkipped()
        {
            // H6 -- the invariant is only total because there is no third "skipped" category.
            Matches([UserWith("u12", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9")]);

            var result = await Create().PreviewBulkRoleChangeAsync(Add("r10", "u12"));

            result.MatchedCount.Should().Be(1);
            result.AffectedCount.Should().Be(1);
            result.UnchangedCount.Should().Be(0);
        }

        [Fact]
        public async Task Preview_EmptyMatch_IsASuccessWithZeroes()
        {
            // H8 -- an empty match is not an error; the console shows "0 users" rather than a failure.
            Matches([]);

            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "zzzzz" }, add: ["viewer"]));

            result.IsSuccess.Should().BeTrue();
            result.MatchedCount.Should().Be(0);
            result.AffectedCount.Should().Be(0);
            result.UnchangedCount.Should().Be(0);
        }

        [Fact]
        public async Task Preview_NeverReadsOrWritesPermissions()
        {
            // C11 -- this feature is roles-only; MaxPermissionsPerUser stays the only cap in the repo
            // and is not consulted here.
            var user = UserWith("u1", "member");
            user.Permissions[Org] = ["p1", "p2"];
            Matches([user]);

            await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1"));

            user.Permissions[Org].Should().Equal("p1", "p2");
        }

        // ---------- Preview: validation ----------

        [Fact]
        public async Task Preview_BlankOrganizationId_IsRejectedBeforeAnythingIsResolved()
        {
            // C1
            var result = await Create().PreviewBulkRoleChangeAsync(new BulkRoleChangeRequest
            {
                OrganizationId = "  ",
                AddRoles = ["viewer"],
                Target = new BulkRoleTarget { UserIds = ["u1"] }
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["OrganizationId"].Should().Be("OrganizationId is required");
            _userRepo.Verify(r => r.GetUsersAsync<User, GetUsersRequest>(
                It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()), Times.Never);
        }

        [Fact]
        public async Task Preview_CallerFromAnotherOrganization_IsRejectedWithTheExistingMessage()
        {
            // C2 -- the same guard and the same wording the single-user path already uses, so the
            // rule cannot drift between the two.
            InstallContext("org_northwind");
            Matches([UserWith("u1", "member")]);

            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1"));

            result.Errors!["OrganizationId"].Should().Be("Other org user can not add/update");
        }

        [Fact]
        public async Task Preview_UnknownOrganization_IsRejected()
        {
            // C3
            _resourceRepo.Setup(r => r.GetOrganizationById(It.IsAny<string>())).ReturnsAsync((Organization)null!);

            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1"));

            result.Errors!["OrganizationId"].Should().Be("Organization not found");
        }

        [Fact]
        public async Task Preview_DefaultOrganization_SkipsTheExistenceLookup()
        {
            // A3 -- "default" is the tenant-wide organization and has no Organization document, which
            // is what makes multi-org-disabled tenants work without a special case in the console.
            Matches([UserWith("u1", "member")]);

            var result = await Create().PreviewBulkRoleChangeAsync(new BulkRoleChangeRequest
            {
                OrganizationId = "default",
                AddRoles = ["viewer"],
                Target = new BulkRoleTarget { UserIds = ["u1"] }
            });

            result.IsSuccess.Should().BeTrue();
            _resourceRepo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task Preview_BothOrNeitherTargetForm_IsRejected(bool withIds, bool withFilter)
        {
            // C4 -- a caller that sends both has two intents; the server does not guess.
            var result = await Create().PreviewBulkRoleChangeAsync(new BulkRoleChangeRequest
            {
                OrganizationId = Org,
                AddRoles = ["viewer"],
                Target = new BulkRoleTarget
                {
                    UserIds = withIds ? ["u1"] : null,
                    Filter = withFilter ? new GetUsersFilter { Name = "a" } : null
                }
            });

            result.Errors!["Target"].Should().Be("Exactly one of userIds or filter is required");
        }

        [Fact]
        public async Task Preview_EmptyRoleLists_AreRejected()
        {
            // C5
            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "a" }));

            result.Errors!["Roles"].Should().Be("At least one role to add or remove is required");
        }

        [Fact]
        public async Task Preview_SameRoleAddedAndRemoved_IsRejected()
        {
            // C5
            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "a" }, add: ["viewer"], remove: ["viewer"]));

            result.Errors!["Roles"].Should().Be("The same role cannot be both added and removed");
        }

        [Fact]
        public async Task Preview_DuplicateRole_IsRejected()
        {
            // C5
            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "a" }, add: ["viewer", "viewer"]));

            result.Errors!["Roles"].Should().Be("Duplicate role in the same request");
        }

        [Fact]
        public async Task Preview_MoreThanFiftyRoles_IsRejectedAsAMalformedPayloadNotACap()
        {
            // C5/A7 -- a request-shape guard. It is deliberately far above any plausible delta so it
            // can never be mistaken for a rule about how many roles a person may hold.
            var roles = Enumerable.Range(0, 51).Select(i => $"r{i}").ToArray();

            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "a" }, add: roles));

            result.Errors!["Roles"].Should().Be("A maximum of 50 roles is allowed per request");
        }

        [Fact]
        public async Task Preview_MoreThanAThousandUserIds_IsRejected()
        {
            // C6
            var ids = Enumerable.Range(0, 1001).Select(i => $"u{i}").ToArray();

            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", ids));

            result.Errors!["Target"].Should().Be("A maximum of 1000 userIds is allowed per request");
        }

        [Fact]
        public async Task Preview_DuplicateUserId_IsRejected()
        {
            // C6
            var result = await Create().PreviewBulkRoleChangeAsync(Add("viewer", "u1", "u1"));

            result.Errors!["Target"].Should().Be("Duplicate userId in the same request");
        }

        [Fact]
        public async Task Preview_OverTheMatchedCap_ReturnsNoCountsAtAll()
        {
            // C7/A5 -- a truncated count would be a number the operator acts on that does not
            // describe what would happen, which is the one failure this endpoint exists to prevent.
            Matches([], totalCount: 5001);

            var result = await Create().PreviewBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Roles = ["member"] }, add: ["viewer"]));

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Matched"].Should().Be("This filter matches more than 5000 users. Narrow it and try again.");
            result.MatchedCount.Should().Be(0);
            result.AffectedCount.Should().Be(0);
            result.UnchangedCount.Should().Be(0);
        }

        // ---------- Submit ----------

        [Fact]
        public async Task Submit_QueuesTheWorkAndWritesNothingInTheRequestPath()
        {
            // H1
            Matches([UserWith("u1", "member"), UserWith("u2", "member"), UserWith("u3", "member")]);

            var result = await Create().SubmitBulkRoleChangeAsync(Add("viewer", "u1", "u2", "u3"));

            result.IsSuccess.Should().BeTrue();
            result.BatchId.Should().NotBeNullOrWhiteSpace();
            result.MatchedCount.Should().Be(3);
            _published.Should().HaveCount(1);
            _published[0].ConsumerName.Should().Be(IdpConstants.IamBulkRoleQueue);
            _published[0].Payload.OrganizationId.Should().Be(Org);
            _published[0].Payload.AddRoles.Should().Equal("viewer");
            _published[0].Payload.UserIds.Should().Equal("u1", "u2", "u3");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Submit_LargeTarget_IsChunkedIntoDisjointMessagesSharingOneBatchId()
        {
            // H2 / example 9 -- disjointness is what guarantees no user is written twice for one
            // submit, independently of how the broker schedules the chunks.
            Matches(Enumerable.Range(0, 1200).Select(i => UserWith($"u{i}", "member")));

            var result = await Create().SubmitBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Roles = ["member"] }, add: ["viewer"]));

            result.MatchedCount.Should().Be(1200);
            _published.Should().HaveCount(3);
            _published.Select(m => m.Payload.UserIds.Count).Should().Equal(500, 500, 200);
            _published.Select(m => m.Payload.BatchId).Distinct().Should().HaveCount(1);
            _published.SelectMany(m => m.Payload.UserIds).Distinct().Should().HaveCount(1200);
        }

        [Fact]
        public async Task Submit_EmptyMatch_IsAcceptedAndQueuesNothing()
        {
            // H7 -- accepting a no-op costs nothing and keeps the console's success path uniform.
            Matches([]);

            var result = await Create().SubmitBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Name = "zzzzz" }, add: ["viewer"]));

            result.IsSuccess.Should().BeTrue();
            result.MatchedCount.Should().Be(0);
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task Submit_CarriesTheTenantIdBecauseTheWorkerHasNoCaller()
        {
            Matches([UserWith("u1", "member")]);

            await Create().SubmitBulkRoleChangeAsync(Add("viewer", "u1"));

            _published[0].Payload.TenantId.Should().Be("tenant-1");
        }

        [Theory]
        [InlineData("Roles", "The same role cannot be both added and removed")]
        public async Task Submit_RejectsWithTheSameMessagesThePreviewUses_AndQueuesNothing(string field, string message)
        {
            // C1 -- the two endpoints share one validator, so their error strings cannot drift.
            var request = ByFilter(new GetUsersFilter { Name = "a" }, add: ["viewer"], remove: ["viewer"]);

            var preview = await Create().PreviewBulkRoleChangeAsync(request);
            var submit = await Create().SubmitBulkRoleChangeAsync(request);

            submit.Errors![field].Should().Be(message);
            submit.Errors[field].Should().Be(preview.Errors![field]);
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task Submit_OverTheMatchedCap_IsRejectedAndQueuesNothing()
        {
            // C1 (the Matched branch)
            Matches([], totalCount: 5001);

            var result = await Create().SubmitBulkRoleChangeAsync(
                ByFilter(new GetUsersFilter { Roles = ["member"] }, add: ["viewer"]));

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Matched"].Should().Be("This filter matches more than 5000 users. Narrow it and try again.");
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task Submit_PublishingThrows_ReportsFailureRatherThanClaimingItWasSubmitted()
        {
            // C2 -- the console must never show "submitted successfully" for work that was never
            // queued. There is no progress feed to correct the impression later.
            Matches([UserWith("u1", "member")]);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BulkUserRoleChangeEvent>>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

            var result = await Create().SubmitBulkRoleChangeAsync(Add("viewer", "u1"));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Queue");
        }

        // ---------- Apply (what the worker consumer drives) ----------

        [Fact]
        public async Task Apply_WritesOnlyTheUsersThatActuallyChange()
        {
            // H3, H4 -- a no-op costs no write at all, which is what keeps a broad "add Viewer to
            // everyone" cheap on a mostly-already-there set.
            var changing = UserWith("u1", "member");
            var noOp = UserWith("u2", "member", "viewer");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(changing);
            _userRepo.Setup(r => r.GetUserByIdAsync("u2")).ReturnsAsync(noOp);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1", "u2"]
            });

            changing.Roles[Org].Should().Equal("member", "viewer");
            _userRepo.Verify(r => r.UpdateUserAsync(changing), Times.Once);
            _userRepo.Verify(r => r.UpdateUserAsync(noOp), Times.Never);
        }

        [Fact]
        public async Task Apply_AddsTheMembershipAndLeavesOtherOrganizationsAlone()
        {
            // H5, C9
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = ["org_northwind"],
                Roles = new Dictionary<string, List<string>> { ["org_northwind"] = ["admin"] }
            };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1"]
            });

            user.OrganizationIds.Should().Contain(Org);
            user.Roles[Org].Should().Equal("viewer");
            user.Roles["org_northwind"].Should().Equal("admin");
        }

        [Fact]
        public async Task Apply_MissingUser_IsSkippedAndTheRestOfTheChunkStillRuns()
        {
            // C3
            var survivor = UserWith("u2", "member");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);
            _userRepo.Setup(r => r.GetUserByIdAsync("u2")).ReturnsAsync(survivor);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1", "u2"]
            });

            _userRepo.Verify(r => r.UpdateUserAsync(survivor), Times.Once);
        }

        [Fact]
        public async Task Apply_OneFailingWrite_DoesNotAbortTheChunkOrRethrow()
        {
            // C4/A6 -- rethrowing would have the broker redeliver the chunk, re-scanning every
            // healthy user for one bad id and looping forever on a permanently failing one.
            var bad = UserWith("u1", "member");
            var good = UserWith("u2", "member");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(bad);
            _userRepo.Setup(r => r.GetUserByIdAsync("u2")).ReturnsAsync(good);
            _userRepo.Setup(r => r.UpdateUserAsync(bad)).ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1", "u2"]
            });

            await act.Should().NotThrowAsync();
            _userRepo.Verify(r => r.UpdateUserAsync(good), Times.Once);
        }

        [Fact]
        public async Task Apply_UpdateReturningFalse_DoesNotAbortTheChunk()
        {
            // C4 -- the repository's "rejected" answer is treated exactly like a thrown failure.
            var rejected = UserWith("u1", "member");
            var good = UserWith("u2", "member");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(rejected);
            _userRepo.Setup(r => r.GetUserByIdAsync("u2")).ReturnsAsync(good);
            _userRepo.Setup(r => r.UpdateUserAsync(rejected)).ReturnsAsync(false);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1", "u2"]
            });

            _userRepo.Verify(r => r.UpdateUserAsync(good), Times.Once);
        }

        [Fact]
        public async Task Apply_PastNineRoles_IsWrittenWithoutCapping()
        {
            // C5 -- the affectedCount the operator approved in the preview is the number actually
            // written; a cap here would silently break that promise with nobody watching.
            var user = UserWith("u12", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9");
            _userRepo.Setup(r => r.GetUserByIdAsync("u12")).ReturnsAsync(user);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["r10"],
                UserIds = ["u12"]
            });

            user.Roles[Org].Should().HaveCount(10);
            _userRepo.Verify(r => r.UpdateUserAsync(user), Times.Once);
        }

        [Fact]
        public async Task Apply_Redelivered_ConvergesWithoutASecondWrite()
        {
            // C8
            var user = UserWith("u1", "member");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            var message = new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1"]
            };

            var service = Create();
            await service.ApplyBulkRoleChangeAsync(message);
            await service.ApplyBulkRoleChangeAsync(message);

            user.Roles[Org].Should().Equal("member", "viewer");
            _userRepo.Verify(r => r.UpdateUserAsync(user), Times.Once);
        }

        [Fact]
        public async Task Apply_NeverTouchesPermissions()
        {
            // C11 / SPEC20 "must not break" -- roles only, so MaxPermissionsPerUser stays the one cap
            // in the repo and is never consulted, extended or moved by this path.
            var user = UserWith("u1", "member");
            user.Permissions[Org] = ["p1"];
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = Org,
                AddRoles = ["viewer"],
                UserIds = ["u1"]
            });

            user.Permissions[Org].Should().Equal("p1");
        }

        // ---------- The single-user path is untouched ----------

        [Fact]
        public async Task UpdateUserAccessControl_StillReplacesAndStillEnforcesThePermissionCap()
        {
            // The "purely additive" claim, enforced by a test rather than by review: the old flat
            // request still replaces the role list, and the permission cap it guards is unchanged.
            var user = UserWith("u1", "member", "editor");
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            var replaced = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "u1",
                OrganizationId = Org,
                Roles = ["viewer"]
            });

            replaced.IsSuccess.Should().BeTrue();
            user.Roles[Org].Should().Equal("viewer");

            var capped = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "u1",
                OrganizationId = Org,
                Roles = ["viewer"],
                Permissions = ["p1", "p2", "p3", "p4", "p5", "p6"]
            });

            capped.IsSuccess.Should().BeFalse();
            capped.Errors!["Permissions"].Should().Contain("maximum of 5 permissions");
        }
    }
}
