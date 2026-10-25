using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// access grant on every user path: users/create (new and existing email),
    /// users/{id}, me, activate, deactivate, users/access, users/revoke-access, users/roles/bulk
    /// (preview, submit and the worker's per-user re-check). The caller "caller" holds "manager"
    /// in org-acme over the standard tree.
    /// </summary>
    public sealed class UserManagementAccessGrantTests : IDisposable
    {
        private const string Org = AccessGrantTestKit.Org;

        private readonly AccessGrantTestKit _kit = new AccessGrantTestKit().WithStandardTree();
        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IValidator<UpdateMyAccountRequest>> _myAccountValidator = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly List<User> _created = [];
        private readonly List<BulkUserRoleChangeEvent> _bulk = [];

        public UserManagementAccessGrantTests()
        {
            _kit.WithUser("caller", ["manager"], attributes: Access("eu"));
            AccessGrantTestKit.SignIn("caller");

            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>())).Returns(new ValidationResult());
            _myAccountValidator.Setup(v => v.Validate(It.IsAny<UpdateMyAccountRequest>())).Returns(new ValidationResult());
            _userRepo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => _kit.FindUser(id)!);
            _userRepo.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((string email) => _kit.FindUser(email.Split('@')[0])!);
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).Callback<User>(_created.Add).ReturnsAsync(true);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true });
            _resourceRepo.Setup(r => r.GetOrganizationById(It.IsAny<string>())).ReturnsAsync((string id) => new Organization { ItemId = id, Name = id });
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BulkUserRoleChangeEvent>>()))
                .Callback<ConsumerMessage<BulkUserRoleChangeEvent>>(m => _bulk.Add(m.Payload))
                .Returns(Task.CompletedTask);
        }

        public void Dispose() => AccessGrantTestKit.SignOut();

        private UserManagementMutationService Create(bool wired = true) =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object,
                _myAccountValidator.Object, _iam.Object, _userRepo.Object, _message.Object, Mock.Of<ICacheClient>(),
                Mock.Of<ITenants>(), Mock.Of<IUserActivityDispatcher>(), null, _resourceRepo.Object,
                accessGrant: wired ? _kit.Service() : null);

        private static Dictionary<string, object> Access(params string[] regions) =>
            new()
            {
                [AccessAttributes.Key] = new Dictionary<string, object>
                {
                    [Org] = new Dictionary<string, object> { ["region"] = regions.Cast<object>().ToList() }
                }
            };

        private static void ShouldBeRefused(BaseResponse result) =>
            result.Errors.Should().ContainKey(AccessGrantErrors.GrantNotAllowed);

        // ---------- users/create ----------

        [Fact]
        public async Task CreateUserWithARoleTheCallerCannotGiveIsRefused()
        {
            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = "new@example.com", OrganizationId = Org, Roles = ["owner"] });

            ShouldBeRefused(result);
            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task CreateUserWithinTheCallersPowerIsCreated()
        {
            var result = await Create().CreateUserAsync(new CreateUserRequest
            {
                Email = "new@example.com",
                OrganizationId = Org,
                Roles = ["viewer", "auditor"],
                Permissions = ["write"],
                Attributes = Access("eu")
            });

            result.IsSuccess.Should().BeTrue();
            _created.Should().ContainSingle();
        }

        [Fact]
        public async Task CreateUserWithAnAccessValueTheCallerLacksIsRefused()
        {
            var result = await Create().CreateUserAsync(new CreateUserRequest
            {
                Email = "new@example.com",
                OrganizationId = Org,
                Roles = ["viewer"],
                Attributes = Access("apac")
            });

            ShouldBeRefused(result);
        }

        [Fact]
        public async Task CreateUserForAnExistingEmailIsCheckedLikeAnAccessChange()
        {
            _kit.WithUser("boss", ["owner"]);

            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = "boss@example.com", OrganizationId = Org, Roles = ["viewer"] });

            ShouldBeRefused(result);
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task CreateUserForAnExistingEmailThatRemovesARoleEndsTheirSessions()
        {
            _kit.WithUser("member", ["viewer", "auditor"]);

            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = "member@example.com", OrganizationId = Org, Roles = ["viewer"] });

            result.IsSuccess.Should().BeTrue();
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("member", "access_replaced"), Times.Once);
        }

        // ---------- users/{id} and me ----------

        [Fact]
        public async Task UpdateUserAboveTheCallerIsRefused()
        {
            _kit.WithUser("boss", ["owner"]);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "boss", FirstName = "x" });

            ShouldBeRefused(result);
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserMayNotGiveAnAccessValueTheCallerLacks()
        {
            _kit.WithUser("junior", ["viewer"]);

            (await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "junior", Attributes = Access("apac") })).Errors
                .Should().ContainKey(AccessGrantErrors.GrantNotAllowed);
            (await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "junior", Attributes = Access("eu") })).IsSuccess
                .Should().BeTrue();
        }

        [Fact]
        public async Task UpdatingYourselfThroughTheAdminEndpointIsRefused()
        {
            ShouldBeRefused(await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "caller", FirstName = "me" }));
        }

        [Fact]
        public async Task MeMayDropOwnAccessValuesButNeverAddOne()
        {
            ShouldBeRefused(await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { Attributes = Access("eu", "apac") }));
            (await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { Attributes = Access() })).IsSuccess.Should().BeTrue();
        }

        // ---------- activate / deactivate ----------

        [Fact]
        public async Task ActivateAndDeactivateAreManagingTheUser()
        {
            var boss = _kit.WithUser("boss", ["owner"]);
            boss.Active = false;

            ShouldBeRefused(await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "boss" }));
            ShouldBeRefused(await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "boss", Reason = "back" }));
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        // ---------- users/access ----------

        [Fact]
        public async Task AccessChangeIsCheckedOnTheDeltaOnly()
        {
            _kit.WithUser("member", ["viewer"]);

            var allowed = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["viewer", "auditor"] });
            var refused = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["owner"] });

            allowed.IsSuccess.Should().BeTrue();
            ShouldBeRefused(refused);
        }

        [Fact]
        public async Task AccessChangeThatRemovesARoleEndsTheUsersSessions()
        {
            _kit.WithUser("member", ["viewer", "auditor"]);

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["auditor"] });

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("member", "access_replaced"), Times.Once);
        }

        [Fact]
        public async Task AccessChangeThatOnlyAddsEndsNoSession()
        {
            _kit.WithUser("member", ["viewer"]);

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["viewer", "auditor"] });

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RefusedAccessChangeIsLogged()
        {
            _kit.WithUser("member", ["viewer"]);

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["owner"] });

            ShouldBeRefused(result);
            _kit.Logged($"AccessGrant denied R4-AccessDelta caller {Org} owner").Should().BeTrue();
        }

        [Fact]
        public async Task AccessChangeWarnsWhenAnAddedRoleCarriesPolicyPower()
        {
            _kit.PolicyRoles.Add("owner");
            _kit.WithUser("member", ["viewer"]);

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["owner"] });

            _kit.Logged("AccessGrant.RoleCarriesPolicyPower owner").Should().BeTrue();
        }

        [Fact]
        public async Task SystemUserAccessChangeIsUnchanged()
        {
            AccessGrantTestKit.SignIn("caller", "default");
            _kit.WithUser("member", ["viewer"], organizationId: "default");

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = "default", Roles = ["anything"] });

            result.IsSuccess.Should().BeTrue();
            _kit.Repository.Verify(r => r.GetRolesAsync(It.IsAny<string>()), Times.Never);
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UnwiredServiceChangesNothing()
        {
            _kit.WithUser("member", ["viewer"]);

            var result = await Create(wired: false).UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "member", OrganizationId = Org, Roles = ["owner"] });

            result.IsSuccess.Should().BeTrue();
        }

        // ---------- users/revoke-access ----------

        [Fact]
        public async Task RevokeIsManagingTheUserAndEndsTheirSessions()
        {
            _kit.WithUser("boss", ["owner"]);
            _kit.WithUser("member", ["viewer"]);

            ShouldBeRefused(await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "boss", OrganizationId = Org }));
            (await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "member", OrganizationId = Org })).IsSuccess
                .Should().BeTrue();

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("member", "access_revoked"), Times.Once);
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("boss", It.IsAny<string>()), Times.Never);
        }

        // ---------- users/roles/bulk ----------

        private void BulkMatches(params User[] users) =>
            _userRepo.Setup(r => r.GetUsersAsync<User, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((users.AsQueryable(), users.Length));

        [Fact]
        public async Task BulkPreviewAndSubmitRefuseARoleTheCallerCannotGiveOrTake()
        {
            BulkMatches(_kit.WithUser("member", ["viewer"]));
            var add = new BulkRoleChangeRequest { OrganizationId = Org, AddRoles = ["owner"], Target = new BulkRoleTarget { UserIds = ["member"] } };
            var remove = new BulkRoleChangeRequest { OrganizationId = Org, RemoveRoles = ["admin"], Target = new BulkRoleTarget { UserIds = ["member"] } };

            (await Create().PreviewBulkRoleChangeAsync(add)).Errors.Should().ContainKey(AccessGrantErrors.GrantNotAllowed);
            (await Create().SubmitBulkRoleChangeAsync(add)).Errors.Should().ContainKey(AccessGrantErrors.GrantNotAllowed);
            (await Create().SubmitBulkRoleChangeAsync(remove)).Errors.Should().ContainKey(AccessGrantErrors.GrantNotAllowed);
            _bulk.Should().BeEmpty();
        }

        [Fact]
        public async Task BulkSubmitCarriesTheSubmitterOnTheEvent()
        {
            BulkMatches(_kit.WithUser("member", ["viewer"]));

            await Create().SubmitBulkRoleChangeAsync(new BulkRoleChangeRequest { OrganizationId = Org, AddRoles = ["auditor"], Target = new BulkRoleTarget { UserIds = ["member"] } });

            _bulk.Should().ContainSingle();
            _bulk[0].RequestedBy.Should().Be("caller");
            _bulk[0].RequestedByOrganizationId.Should().Be(Org);
            _bulk[0].RequestedBySystemCaller.Should().BeFalse();
        }

        private static BulkUserRoleChangeEvent Chunk(bool? system, List<string> add, List<string> remove, params string[] userIds) =>
            new()
            {
                BatchId = "b_1",
                OrganizationId = Org,
                TenantId = AccessGrantTestKit.Tenant,
                AddRoles = add,
                RemoveRoles = remove,
                UserIds = userIds.ToList(),
                RequestedBy = "caller",
                RequestedByOrganizationId = Org,
                RequestedBySystemCaller = system
            };

        [Fact]
        public async Task BulkConsumerReChecksEveryUserAgainstTheSubmitter()
        {
            BlocksContext.SetContext(null);
            _kit.WithUser("member", ["viewer"]);
            _kit.WithUser("boss", ["owner"]);

            await Create().ApplyBulkRoleChangeAsync(Chunk(false, ["auditor"], [], "member", "boss"));

            _kit.FindUser("member")!.Roles[Org].Should().Contain("auditor");
            _kit.FindUser("boss")!.Roles[Org].Should().NotContain("auditor", "boss holds more than the submitter");
        }

        [Fact]
        public async Task BulkConsumerRefusesTheWholeChunkWhenTheRolesNoLongerPass()
        {
            BlocksContext.SetContext(null);
            _kit.WithUser("member", ["viewer"]);
            _kit.WithUser("caller", ["auditor"]); // the submitter lost "manager" while the batch waited

            await Create().ApplyBulkRoleChangeAsync(Chunk(false, ["viewer"], ["auditor"], "member"));

            _kit.FindUser("member")!.Roles[Org].Should().Equal("viewer");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task BulkConsumerEndsSessionsOfUsersWhoLostARole()
        {
            BlocksContext.SetContext(null);
            _kit.WithUser("member", ["viewer", "auditor"]);
            _kit.WithUser("other", ["auditor"]);

            await Create().ApplyBulkRoleChangeAsync(Chunk(false, [], ["viewer"], "member", "other"));

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("member", "bulk_role_change"), Times.Once);
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("other", It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task BulkMessagesQueuedBeforeTheSubmitterWasRecordedApplyAsBefore()
        {
            BlocksContext.SetContext(null);
            _kit.WithUser("boss", ["owner"]);

            await Create().ApplyBulkRoleChangeAsync(Chunk(null, ["auditor"], [], "boss"));

            _kit.FindUser("boss")!.Roles[Org].Should().Contain("auditor");
        }

        // ---------- users/roles/bulk by a client credential ----------

        private const string Client = "svc";

        private static BulkUserRoleChangeEvent ClientChunk(List<string> add, params string[] userIds)
        {
            var chunk = Chunk(false, add, [], userIds);
            chunk.RequestedBy = null;
            chunk.RequestedByClientId = Client;
            return chunk;
        }

        [Fact]
        public async Task BulkSubmitByAClientCarriesTheClientFromTheContext()
        {
            _kit.WithClient(Client, ["manager"]);
            AccessGrantTestKit.SignInClient(Client);
            BulkMatches(_kit.WithUser("member", ["viewer"]));

            await Create().SubmitBulkRoleChangeAsync(new BulkRoleChangeRequest { OrganizationId = Org, AddRoles = ["auditor"], Target = new BulkRoleTarget { UserIds = ["member"] } });

            _bulk.Should().ContainSingle();
            _bulk[0].RequestedBy.Should().BeNullOrEmpty();
            _bulk[0].RequestedByClientId.Should().Be(Client);
            _bulk[0].RequestedBySystemCaller.Should().BeFalse();
        }

        [Fact]
        public async Task BulkSubmitByAUserCarriesNoClient()
        {
            BulkMatches(_kit.WithUser("member", ["viewer"]));

            await Create().SubmitBulkRoleChangeAsync(new BulkRoleChangeRequest { OrganizationId = Org, AddRoles = ["auditor"], Target = new BulkRoleTarget { UserIds = ["member"] } });

            _bulk.Should().ContainSingle();
            _bulk[0].RequestedByClientId.Should().BeNull();
        }

        [Fact]
        public async Task BulkConsumerAppliesAClientChangeWithinItsGrants()
        {
            BlocksContext.SetContext(null);
            _kit.WithClient(Client, ["manager"]);
            _kit.WithUser("member", ["viewer"]);
            _kit.WithUser("boss", ["owner"]);

            await Create().ApplyBulkRoleChangeAsync(ClientChunk(["auditor"], "member", "boss"));

            _kit.FindUser("member")!.Roles[Org].Should().Contain("auditor");
            _kit.FindUser("boss")!.Roles[Org].Should().NotContain("auditor", "boss holds more than the client");
        }

        [Fact]
        public async Task BulkConsumerRefusesAClientChangeBeyondItsGrants()
        {
            BlocksContext.SetContext(null);
            _kit.WithClient(Client, ["manager"]);
            _kit.WithUser("member", ["viewer"]);

            await Create().ApplyBulkRoleChangeAsync(ClientChunk(["owner"], "member"));

            _kit.FindUser("member")!.Roles[Org].Should().Equal("viewer");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Theory]
        [InlineData(false, Org)]
        [InlineData(true, "org-other")]
        public async Task BulkConsumerRefusesAnInactiveOrOtherOrganizationClient(bool isActive, string clientOrg)
        {
            BlocksContext.SetContext(null);
            _kit.WithClient(Client, ["manager"], organizationId: clientOrg, isActive: isActive);
            _kit.WithUser("member", ["viewer"]);

            await Create().ApplyBulkRoleChangeAsync(ClientChunk(["auditor"], "member"));

            _kit.FindUser("member")!.Roles[Org].Should().Equal("viewer");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task BulkConsumerRefusesAMissingClient()
        {
            BlocksContext.SetContext(null);
            _kit.WithUser("member", ["viewer"]);

            await Create().ApplyBulkRoleChangeAsync(ClientChunk(["auditor"], "member"));

            _kit.FindUser("member")!.Roles[Org].Should().Equal("viewer");
        }

        [Fact]
        public async Task BulkMessagesWithNoUserAndNoClientAreRefusedAsBefore()
        {
            BlocksContext.SetContext(null);
            _kit.WithClient(Client, ["manager"]);
            _kit.WithUser("member", ["viewer"]);
            var chunk = Chunk(false, ["auditor"], [], "member");
            chunk.RequestedBy = null;

            await Create().ApplyBulkRoleChangeAsync(chunk);

            _kit.FindUser("member")!.Roles[Org].Should().Equal("viewer");
        }

        [Fact]
        public async Task BulkConsumerIgnoresAClientIdWhenAUserSubmitted()
        {
            BlocksContext.SetContext(null);
            _kit.WithClient(Client, ["admin", "owner"]);
            _kit.WithUser("member", ["viewer"]);
            _kit.WithUser("boss", ["owner"]);
            var chunk = Chunk(false, ["auditor"], [], "boss");
            chunk.RequestedByClientId = Client;

            await Create().ApplyBulkRoleChangeAsync(chunk);

            _kit.FindUser("boss")!.Roles[Org].Should().NotContain("auditor", "the user submitter's power applies, not the client's");
        }
    }
}
