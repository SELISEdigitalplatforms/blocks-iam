using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Actions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// IAM tells the tenant's webhooks and workflows about a user change only after the change was
    /// saved. A failed save must stay silent: a target must never hear about a change that did not happen.
    /// </summary>
    public sealed class UserActionPublishingTests : IDisposable
    {
        private const string Org = "org-x";

        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IValidator<UpdateMyAccountRequest>> _myAccountValidator = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly Mock<IActionPublisher> _actions = new();
        private readonly List<(string Action, string Json)> _published = new();

        public UserActionPublishingTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>())).Returns(new ValidationResult());
            _myAccountValidator.Setup(v => v.Validate(It.IsAny<UpdateMyAccountRequest>())).Returns(new ValidationResult());
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserStatusChangedEvent>>())).Returns(Task.CompletedTask);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new IamTenantConfiguration());
            _resourceRepo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync(new Organization { ItemId = Org, Name = "Org X" });
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            _actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) =>
                    _published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null!);
            BlocksContext.IsTestMode = false;
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object,
                _myAccountValidator.Object, new Mock<IIdentityAccessManagementService>().Object, _userRepo.Object,
                _message.Object, new Mock<ICacheClient>().Object, new Mock<ITenants>().Object,
                new Mock<IUserActivityDispatcher>().Object, null, _resourceRepo.Object,
                actionPublisher: _actions.Object);

        private static User Member(string id = "target-1") => new()
        {
            ItemId = id,
            OrganizationIds = ["default", Org],
            Roles = new() { { Org, ["admin"] } },
            Permissions = new() { { Org, ["read"] } }
        };

        private void SaveFails() => _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

        // ---------- create ----------

        [Fact]
        public async Task CreateUser_PublishesCreatedWithRolesAndCreationType()
        {
            var result = await Create().CreateUserAsync(new CreateUserRequest
            {
                Email = "new@example.com",
                OrganizationId = "default",
                Roles = ["viewer"],
                Permissions = ["read"],
                MfaEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserCreated,
                $"{{\"userId\":\"{result.ItemId}\",\"roles\":[\"viewer\"],\"permissions\":[\"read\"],\"creationType\":\"Portal\",\"mfaEnabled\":true}}"));
        }

        [Fact]
        public async Task CreateUserByEmail_PublishesCreatedWithServiceCreationType()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "invited-1", Active = true, IsVerified = true });
            _userRepo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(new IamConfiguration { ActivationUrlLifetimeInMinutes = 60 });

            var result = await Create().CreateUserByEmailAsync(new CreateUserByEmailEvent
            {
                Email = "invited@example.com",
                EventQueue = "queue",
                EventType = "invite",
                Roles = ["viewer"],
                Permissions = ["read"],
                TenantId = "tenant-1"
            });

            result.Should().BeTrue();
            var (name, data) = _published.Should().ContainSingle().Subject;
            name.Should().Be(IamActionNames.UserCreated);
            data.Should().Contain("\"roles\":[\"viewer\"]").And.Contain("\"creationType\":\"Service\"");
        }

        [Fact]
        public async Task CreateUser_ForAnEmailThatAlreadyHasAnAccount_PublishesNothing()
        {
            _userRepo.Setup(r => r.GetUserByEmailAsync("old@example.com")).ReturnsAsync(new User { ItemId = "old-1", OrganizationIds = ["default"] });

            var result = await Create().CreateUserAsync(new CreateUserRequest { Email = "old@example.com", OrganizationId = Org });

            result.IsSuccess.Should().BeTrue();
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task CreateSignupUserWithoutOrganization_PublishesCreatedWithNoRoles()
        {
            var result = await Create().CreateSignupUserWithoutOrganizationAsync(
                new CreateUserRequest { Email = "signup@example.com", Roles = ["viewer"] });

            result.IsSuccess.Should().BeTrue();
            _published.Should().ContainSingle().Which.Action.Should().Be(IamActionNames.UserCreated);
            _published[0].Json.Should().Contain("\"roles\":[]");
        }

        // ---------- update ----------

        [Fact]
        public async Task UpdateUser_PublishesUpdatedByAdmin_AfterTheSave()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(Member("u1"));

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "X" });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserUpdated, "{\"userId\":\"u1\",\"by\":\"admin\"}"));
        }

        [Fact]
        public async Task UpdateUser_WhenTheSaveFails_PublishesNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(Member("u1"));
            SaveFails();

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "X" });

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateMyAccount_PublishesUpdatedBySelf()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("actor-1")).ReturnsAsync(Member("actor-1"));

            await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { FirstName = "Me" });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserUpdated, "{\"userId\":\"actor-1\",\"by\":\"self\"}"));
        }

        // ---------- activate / deactivate ----------

        [Fact]
        public async Task ActivateUser_PublishesActivatedViaAdmin()
        {
            var user = Member();
            user.Active = false;
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(user);

            var result = await Create().ActivateUserAsync(new ActivateUserByAdminRequest { UserId = "target-1", Reason = "back" });

            result.IsSuccess.Should().BeTrue();
            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserActivated, "{\"userId\":\"target-1\",\"via\":\"admin\"}"));
        }

        [Fact]
        public async Task DeactivateUser_PublishesDeactivated_AndNothingWhenTheSaveFails()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(Member());
            await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "target-1" });
            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserDeactivated, "{\"userId\":\"target-1\"}"));

            _published.Clear();
            SaveFails();
            await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "target-1" });
            _published.Should().BeEmpty();
        }

        // ---------- roles ----------

        [Fact]
        public async Task UpdateAccessControl_PublishesRolesChangedSet()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(Member());

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = Org, Roles = ["editor"], Permissions = ["write"]
            });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserRolesChanged,
                "{\"userId\":\"target-1\",\"change\":\"set\",\"roles\":[\"editor\"],\"permissions\":[\"write\"]}"));
        }

        [Fact]
        public async Task UpdateAccessControl_WhenTheSaveFails_PublishesNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(Member());
            SaveFails();

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = Org, Roles = ["editor"]
            });

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task RevokeAccessControl_PublishesRolesChangedRevokeWithEmptyLists()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(Member());

            await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = Org });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserRolesChanged,
                "{\"userId\":\"target-1\",\"change\":\"revoke\",\"roles\":[],\"permissions\":[]}"));
        }

        [Fact]
        public async Task RevokeAccessControl_WhenTheSaveFails_PublishesNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(Member());
            SaveFails();

            await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = Org });

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task BulkRoleChange_PublishesOncePerAppliedUserOnly()
        {
            var changing = Member("u1");
            var alreadyThere = Member("u2");
            alreadyThere.Roles[Org] = ["admin", "viewer"];
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(changing);
            _userRepo.Setup(r => r.GetUserByIdAsync("u2")).ReturnsAsync(alreadyThere);
            _userRepo.Setup(r => r.GetUserByIdAsync("u3")).ReturnsAsync((User)null!);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1", OrganizationId = Org, AddRoles = ["viewer"], UserIds = ["u1", "u2", "u3"]
            });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserRolesChanged,
                "{\"userId\":\"u1\",\"change\":\"bulk\",\"addRoles\":[\"viewer\"],\"removeRoles\":[],\"batchId\":\"b_1\"}"));
        }
    }
}
