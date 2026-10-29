using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Notifications;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Notifications
{
    /// <summary>Records every notification handed to the sender; can be told to fail.</summary>
    internal sealed class RecordingSender : IBuiltInNotificationSender
    {
        public List<BuiltInNotification> Sent { get; } = new();
        public bool Throw { get; set; }

        public Task<bool> SendAsync(BuiltInNotification notification)
        {
            Sent.Add(notification);
            if (Throw)
            {
                throw new HttpRequestException("notifier down");
            }

            return Task.FromResult(true);
        }

        public static string PayloadJson(BuiltInNotification n) => JsonSerializer.Serialize(n.Payload);
    }

    internal static class TestContext
    {
        public static void Install(string userId = "actor-1", string tenantId = "tenant-1", string orgId = "default")
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        public static void Clear()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Activity rules + worker: password, MFA, activated, session revoked, new login
    // ---------------------------------------------------------------------------------------

    public class ActivityNotificationRulesTests
    {
        private static UserActivity Activity(string evt, string outcome = "success", Dictionary<string, string>? metadata = null) => new()
        {
            ItemId = "a1",
            UserId = "u1",
            ActorUserId = "u1",
            Category = UserActivityCategory.Auth,
            TenantId = "tenant-1",
            Event = evt,
            Outcome = outcome,
            ClientId = "client-1",
            Context = new ActivityContext { IpAddress = "10.0.0.1", UserAgent = "agent/1" },
            Metadata = metadata
        };

        [Theory]
        [InlineData("Reset_Password", "reset")]
        [InlineData("Change_Password", "changed")]
        public void Password_Events_NotifyOwner(string evt, string change)
        {
            var n = ActivityNotificationRules.Build(Activity(evt), null)!;

            n.ConfigurationName.Should().Be("iam-password-changed");
            n.UserIds.Should().Equal("u1");
            n.TenantId.Should().Be("tenant-1");
            RecordingSender.PayloadJson(n).Should().Contain($"\"change\":\"{change}\"");
        }

        [Theory]
        [InlineData("mfa_enrollment_completed", "added", true)]
        [InlineData("mfa_enrollment_failed", "enrolment-failed", false)]
        [InlineData("mfa_method_changed", "method-changed", true)]
        [InlineData("mfa_removed", "removed", true)]
        public void Mfa_Events_NotifyOwner(string evt, string change, bool isSuccess)
        {
            var n = ActivityNotificationRules.Build(Activity(evt), null)!;

            n.ConfigurationName.Should().Be("iam-mfa-changed");
            n.UserIds.Should().Equal("u1");
            var json = RecordingSender.PayloadJson(n);
            json.Should().Contain($"\"change\":\"{change}\"");
            json.Should().Contain($"\"isSuccess\":{isSuccess.ToString().ToLowerInvariant()}");
        }

        [Fact]
        public void UserActivated_NotifiesThatUser()
        {
            var n = ActivityNotificationRules.Build(Activity("USER_ACTIVATED"), null)!;
            n.ConfigurationName.Should().Be("iam-user-activated");
            n.UserIds.Should().Equal("u1");
        }

        [Fact]
        public void SessionRevoked_NotifiesOwner_WithoutSessionId()
        {
            var activity = Activity("user_revoked_session");
            activity.SessionId = "sid-should-not-leak";
            activity.ReasonCode = "user_revoked";

            var n = ActivityNotificationRules.Build(activity, null)!;

            n.ConfigurationName.Should().Be("iam-session-revoked");
            n.UserIds.Should().Equal("u1");
            RecordingSender.PayloadJson(n).Should().NotContain("sid-should-not-leak");
        }

        [Fact]
        public void Login_AfterFailedAttempts_Notifies()
        {
            var n = ActivityNotificationRules.Build(
                Activity("login_success", metadata: new() { ["priorFailedAttempts"] = "2" }),
                null)!;

            n.ConfigurationName.Should().Be("iam-new-login");
            var json = RecordingSender.PayloadJson(n);
            json.Should().Contain("after-failed-attempts");
            json.Should().NotContain("10.0.0.1").And.NotContain("agent/1");
        }

        [Fact]
        public void Login_FromNewDevice_Notifies()
        {
            var n = ActivityNotificationRules.Build(Activity("login_success"), new LoginHistory(true, false))!;

            n.ConfigurationName.Should().Be("iam-new-login");
            RecordingSender.PayloadJson(n).Should().Contain("new-device");
        }

        [Fact]
        public void Login_KnownDevice_OrFirstLogin_OrUnknownHistory_DoesNotNotify()
        {
            ActivityNotificationRules.Build(Activity("login_success"), new LoginHistory(true, true)).Should().BeNull();
            ActivityNotificationRules.Build(Activity("login_success"), new LoginHistory(false, false)).Should().BeNull();
            ActivityNotificationRules.Build(Activity("login_success"), null).Should().BeNull();
        }

        [Fact]
        public void FailedLogin_And_UnmappedEvents_DoNotNotify()
        {
            ActivityNotificationRules.Build(Activity("login_success", outcome: "failure",
                metadata: new() { ["priorFailedAttempts"] = "3" }), null).Should().BeNull();
            ActivityNotificationRules.Build(Activity("login_failure"), null).Should().BeNull();
            ActivityNotificationRules.Build(Activity("ROLE_CREATED"), null).Should().BeNull();
        }

        [Fact]
        public void NeedsLoginHistory_OnlyForSuccessfulLoginWithoutPriorFailures()
        {
            ActivityNotificationRules.NeedsLoginHistory(Activity("login_success")).Should().BeTrue();
            ActivityNotificationRules.NeedsLoginHistory(Activity("login_success", metadata: new() { ["priorFailedAttempts"] = "1" })).Should().BeFalse();
            ActivityNotificationRules.NeedsLoginHistory(Activity("Change_Password")).Should().BeFalse();
        }
    }

    public class UserActivityWorkerNotificationTests : IDisposable
    {
        private readonly Mock<IUserActivityRepository> _repo = new();
        private readonly RecordingSender _sender = new();
        private readonly List<string> _calls = new();

        public UserActivityWorkerNotificationTests()
        {
            TestContext.Install();
            _repo.Setup(r => r.InsertAsync(It.IsAny<UserActivity>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("insert"))
                .Returns(Task.CompletedTask);
        }

        public void Dispose() => TestContext.Clear();

        private UserActivityWorker Create() => new(_repo.Object, NullLogger<UserActivityWorker>.Instance, _sender);

        [Fact]
        public async Task PasswordChange_IsStored_ThenNotified()
        {
            await Create().Consume(new UserActivityEvent { UserId = "u1", Event = "Change_Password", Category = UserActivityCategory.Account, Source = "iam-account" });

            _calls.Should().Equal("insert");
            _sender.Sent.Should().ContainSingle(n => n.ConfigurationName == "iam-password-changed" && n.TenantId == "tenant-1" && n.UserIds.Single() == "u1");
        }

        [Fact]
        public async Task LoginHistory_IsReadBeforeInsert()
        {
            _repo.Setup(r => r.GetLoginHistoryAsync("u1", "login_success", "10.0.0.9", "agent/2", It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("history"))
                .ReturnsAsync(new LoginHistory(true, false));

            await Create().Consume(new UserActivityEvent
            {
                UserId = "u1",
                Event = "login_success",
                Outcome = "success",
                Category = UserActivityCategory.Auth,
                Source = "auth-oidc-login",
                Context = new ActivityContext { IpAddress = "10.0.0.9", UserAgent = "agent/2" }
            });

            _calls.Should().Equal("history", "insert");
            _sender.Sent.Should().ContainSingle(n => n.ConfigurationName == "iam-new-login");
        }

        [Fact]
        public async Task HistoryReadFails_ActivityStillStored_NoNotification()
        {
            _repo.Setup(r => r.GetLoginHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException());

            await Create().Consume(new UserActivityEvent
            {
                UserId = "u1", Event = "login_success", Outcome = "success", Category = UserActivityCategory.Auth, Source = "auth",
                Context = new ActivityContext { IpAddress = "10.0.0.9", UserAgent = "agent/2" }
            });

            _calls.Should().Equal("insert");
            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task SenderThrows_ConsumerDoesNotThrow()
        {
            _sender.Throw = true;

            var act = async () => await Create().Consume(new UserActivityEvent { UserId = "u1", Event = "mfa_removed", Category = UserActivityCategory.Auth, Source = "auth" });

            await act.Should().NotThrowAsync();
            _calls.Should().Equal("insert");
        }

        [Fact]
        public async Task InsertFails_NoNotification()
        {
            _repo.Setup(r => r.InsertAsync(It.IsAny<UserActivity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

            var act = async () => await Create().Consume(new UserActivityEvent { UserId = "u1", Event = "Reset_Password", Category = UserActivityCategory.Account, Source = "iam" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _sender.Sent.Should().BeEmpty();
        }
    }

    public class UserProfileUpdateNotificationTests : IDisposable
    {
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _validator = new();
        private readonly RecordingSender _sender = new();

        public UserProfileUpdateNotificationTests()
        {
            TestContext.Install(orgId: "org-1");
            _validator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>())).Returns(new FluentValidation.Results.ValidationResult());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        }

        public void Dispose() => TestContext.Clear();

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance,
                new Mock<IValidator<CreateUserRequest>>().Object,
                _validator.Object,
                new Mock<IValidator<UpdateMyAccountRequest>>().Object,
                new Mock<IIdentityAccessManagementService>().Object, _userRepo.Object, new Mock<IMessageClient>().Object,
                new Mock<ICacheClient>().Object, new Mock<ITenants>().Object, new Mock<IUserActivityDispatcher>().Object,
                null, new Mock<IResourceRepository>().Object, null, null, null, null, _sender);

        private void UserExists(string id, params string[] orgs) =>
            _userRepo.Setup(r => r.GetUserByIdAsync(id)).ReturnsAsync(new User
            {
                ItemId = id,
                Active = true,
                FirstName = "OldFirst",
                LastName = "SameLast",
                PhoneNumber = "+4100000",
                OrganizationIds = orgs.ToList()
            });

        [Fact]
        public async Task AdminUpdate_NotifiesThatUser_WithFieldNamesOnly()
        {
            UserExists("u1", "org-1");

            var result = await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1",
                OrganizationId = "org-1",
                FirstName = "NewFirst",
                LastName = "SameLast",
                PhoneNumber = "+4199999"
            });

            result.IsSuccess.Should().BeTrue();
            var sent = _sender.Sent.Should().ContainSingle().Subject;
            sent.ConfigurationName.Should().Be("iam-user-updated");
            sent.UserIds.Should().Equal("u1");
            sent.TenantId.Should().Be("tenant-1");
            sent.OrganizationId.Should().Be("org-1");
            var json = RecordingSender.PayloadJson(sent);
            json.Should().Contain("\"changedFields\":[\"firstName\",\"phoneNumber\"]");
            json.Should().NotContain("OldFirst").And.NotContain("NewFirst").And.NotContain("+41");
        }

        [Fact]
        public async Task AdminUpdate_OfOwnAccount_NoNotification()
        {
            TestContext.Install(userId: "u1", orgId: "org-1");
            UserExists("u1", "org-1");

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "org-1", FirstName = "NewFirst" });

            result.IsSuccess.Should().BeTrue();
            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AdminUpdate_NothingChanged_NoNotification()
        {
            UserExists("u1", "org-1");

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "org-1", FirstName = "OldFirst" });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AdminUpdate_WriteFails_NoNotification()
        {
            UserExists("u1", "org-1");
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "org-1", FirstName = "NewFirst" });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AdminUpdate_NotifierFails_UpdateStillSucceeds()
        {
            UserExists("u1", "org-1");
            _sender.Throw = true;

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "org-1", FirstName = "NewFirst" });

            result.IsSuccess.Should().BeTrue();
        }
    }

    public class OidcLoginAuditWriterNotificationTests
    {
        private static async Task<UserActivityEvent?> Write(User user, string eventType)
        {
            var dispatcher = new Mock<IUserActivityDispatcher>();
            UserActivityEvent? captured = null;
            dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation());

            var writer = new OidcLoginAuditWriter(authDomain.Object, dispatcher.Object, NullLogger<OidcLoginAuditWriter>.Instance);
            await writer.WriteAsync("tenant-1", "client-1", user, new DefaultHttpContext().Request, eventType, null);
            return captured;
        }

        [Fact]
        public async Task LoginSuccess_AfterFailures_CarriesPriorFailedAttempts()
        {
            var e = await Write(new User { ItemId = "u1", FailedLoginCount = 2, FailedMfaCount = 1 }, "login_success");
            e!.Metadata.Should().ContainKey("priorFailedAttempts").WhoseValue.Should().Be("3");
        }

        [Fact]
        public async Task LoginSuccess_NoFailures_OrOtherEvents_CarryNoCount()
        {
            (await Write(new User { ItemId = "u1" }, "login_success"))!.Metadata.Should().BeNull();
            (await Write(new User { ItemId = "u1", FailedLoginCount = 4 }, "login_failure"))!.Metadata.Should().BeNull();
        }
    }

    // ---------------------------------------------------------------------------------------
    // User management: deactivate, access update/revoke, bulk role change
    // ---------------------------------------------------------------------------------------

    public class UserManagementNotificationTests : IDisposable
    {
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly RecordingSender _sender = new();

        public UserManagementNotificationTests()
        {
            TestContext.Install();
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        }

        public void Dispose() => TestContext.Clear();

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance,
                new Mock<IValidator<CreateUserRequest>>().Object,
                new Mock<IValidator<UpdateUserRequest>>().Object,
                new Mock<IValidator<UpdateMyAccountRequest>>().Object,
                _iam.Object, _userRepo.Object, _message.Object, new Mock<ICacheClient>().Object,
                new Mock<ITenants>().Object, _activity.Object, null, _resourceRepo.Object, null,
                null, null, null, _sender);

        private void UserExists(string id, params string[] orgs) =>
            _userRepo.Setup(r => r.GetUserByIdAsync(id)).ReturnsAsync(new User
            {
                ItemId = id,
                Active = true,
                OrganizationIds = orgs.ToList(),
                Roles = orgs.ToDictionary(o => o, _ => new List<string> { "member" })
            });

        [Fact]
        public async Task Deactivate_NotifiesThatUser_InActionTenant()
        {
            UserExists("u1");

            var result = await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "u1" });

            result.IsSuccess.Should().BeTrue();
            _sender.Sent.Should().ContainSingle(n =>
                n.ConfigurationName == "iam-user-deactivated" && n.TenantId == "tenant-1" && n.UserIds.Single() == "u1");
        }

        [Fact]
        public async Task Deactivate_UpdateRejected_NoNotification()
        {
            UserExists("u1");
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "u1" });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Deactivate_UnknownUser_NoNotification()
        {
            var result = await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "missing" });

            result.IsSuccess.Should().BeFalse();
            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Deactivate_NotifierFails_ActionStillSucceeds()
        {
            UserExists("u1");
            _sender.Throw = true;

            var result = await Create().DeactivateUserAsync(new DeactivateUserRequest { UserId = "u1" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task AccessUpdate_NotifiesThatUser()
        {
            UserExists("u2", "default");

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "u2",
                OrganizationId = "default",
                Roles = ["admin"]
            });

            result.IsSuccess.Should().BeTrue();
            _sender.Sent.Should().ContainSingle(n => n.ConfigurationName == "iam-user-roles-changed" && n.UserIds.Single() == "u2");
            RecordingSender.PayloadJson(_sender.Sent[0]).Should().Contain("\"change\":\"updated\"");
        }

        [Fact]
        public async Task AccessUpdate_OwnAccount_NoNotification()
        {
            UserExists("actor-1", "default");

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "actor-1",
                OrganizationId = "default",
                Roles = ["admin"]
            });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AccessUpdate_WriteFails_NoNotification()
        {
            UserExists("u2", "default");
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest { UserId = "u2", OrganizationId = "default", Roles = ["admin"] });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AccessRevoke_NotifiesThatUser()
        {
            UserExists("u3", "default");

            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest { UserId = "u3", OrganizationId = "default" });

            result.IsSuccess.Should().BeTrue();
            _sender.Sent.Should().ContainSingle(n => n.ConfigurationName == "iam-user-roles-changed" && n.UserIds.Single() == "u3");
            RecordingSender.PayloadJson(_sender.Sent[0]).Should().Contain("\"change\":\"revoked\"");
        }

        [Fact]
        public async Task BulkApply_NotifiesChangedUsers_AndSubmitterWithCounts()
        {
            UserExists("b1", "org_x");
            UserExists("b2", "org_x");
            _userRepo.Setup(r => r.GetUserByIdAsync("gone")).ReturnsAsync((User)null!);

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_1",
                OrganizationId = "org_x",
                TenantId = "tenant-7",
                AddRoles = ["editor"],
                UserIds = ["b1", "b2", "gone"],
                RequestedBy = "admin-1",
                Part = 2,
                Parts = 3
            });

            var changed = _sender.Sent.Single(n => n.ConfigurationName == "iam-user-roles-changed");
            changed.UserIds.Should().BeEquivalentTo("b1", "b2");
            changed.TenantId.Should().Be("tenant-7");

            var finished = _sender.Sent.Single(n => n.ConfigurationName == "iam-bulk-role-change-finished");
            finished.UserIds.Should().Equal("admin-1");
            var json = RecordingSender.PayloadJson(finished);
            json.Should().Contain("\"applied\":2").And.Contain("\"failed\":1").And.Contain("\"part\":2").And.Contain("\"parts\":3");
            json.Should().Contain("\"isSuccess\":false");
        }

        [Fact]
        public async Task BulkApply_WithoutSubmitter_AppliesAndSkipsFinished()
        {
            UserExists("b1", "org_x");

            await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_2", OrganizationId = "org_x", AddRoles = ["editor"], UserIds = ["b1"]
            });

            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Once);
            _sender.Sent.Should().NotContain(n => n.ConfigurationName == "iam-bulk-role-change-finished");
        }

        [Fact]
        public async Task BulkApply_NotifierFails_ChunkStillApplied()
        {
            UserExists("b1", "org_x");
            _sender.Throw = true;

            var act = async () => await Create().ApplyBulkRoleChangeAsync(new BulkUserRoleChangeEvent
            {
                BatchId = "b_3", OrganizationId = "org_x", AddRoles = ["editor"], UserIds = ["b1"], RequestedBy = "admin-1"
            });

            await act.Should().NotThrowAsync();
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Once);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Resources: permission propagation, organization setup
    // ---------------------------------------------------------------------------------------

    public class ResourceNotificationTests : IDisposable
    {
        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly RecordingSender _sender = new();

        public ResourceNotificationTests()
        {
            TestContext.Install(userId: "root-admin", tenantId: "root-tenant");
        }

        public void Dispose() => TestContext.Clear();

        private ResourceMutationService Create() =>
            new(NullLogger<ResourceMutationService>.Instance, _repo.Object, new Mock<IIdentityAccessManagementService>().Object,
                new Mock<IValidator<CreatePermissionRequest>>().Object, new Mock<IValidator<UpdatePermissionRequest>>().Object,
                new Mock<IValidator<CreateRoleRequest>>().Object, _propagator.Object, _activity.Object,
                new Mock<IUserRepository>().Object, _sender);

        [Fact]
        public async Task Propagation_PartialFailure_NotifiesActor()
        {
            _propagator.Setup(p => p.PropagateAsync(It.IsAny<PermissionMutationForTenantsEvent>()))
                .ReturnsAsync(new PropagationSummary { TenantsAttempted = 3, TenantsSucceeded = 1, TenantsFailed = 2 });

            await Create().ExecutePermissionMutationForTenantsAsync(new PermissionMutationForTenantsEvent { ItemId = "p1", Action = MutationEventType.Update });

            var n = _sender.Sent.Single();
            n.ConfigurationName.Should().Be("iam-permission-propagation-failed");
            n.UserIds.Should().Equal("root-admin");
            n.TenantId.Should().Be("root-tenant");
            RecordingSender.PayloadJson(n).Should().Contain("\"tenantsFailed\":2");
        }

        [Fact]
        public async Task Propagation_AllSucceeded_NoNotification()
        {
            _propagator.Setup(p => p.PropagateAsync(It.IsAny<PermissionMutationForTenantsEvent>()))
                .ReturnsAsync(new PropagationSummary { TenantsAttempted = 2, TenantsSucceeded = 2, TenantsFailed = 0 });

            await Create().ExecutePermissionMutationForTenantsAsync(new PermissionMutationForTenantsEvent { ItemId = "p1", Action = MutationEventType.Create });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task OrganizationSetup_Success_NotifiesCreator()
        {
            _repo.Setup(r => r.GetRolesByOrgAsync("default")).ReturnsAsync(new List<Role>());
            _repo.Setup(r => r.GetPermissionsByOrgAsync("default", It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(new List<Permission>());
            _repo.Setup(r => r.InsertRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(true);

            await Create().ExecuteOrganizationProvisioningAsync(new OrganizationProvisioningEvent { OrganizationId = "org-9", UserId = "creator-1" });

            var n = _sender.Sent.Single();
            n.ConfigurationName.Should().Be("iam-organization-setup-finished");
            n.UserIds.Should().Equal("creator-1");
            RecordingSender.PayloadJson(n).Should().Contain("\"organizationId\":\"org-9\"");
        }

        [Fact]
        public async Task OrganizationSetup_NotifierFails_DoesNotThrow()
        {
            _repo.Setup(r => r.GetRolesByOrgAsync("default")).ReturnsAsync(new List<Role>());
            _repo.Setup(r => r.GetPermissionsByOrgAsync("default", It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(new List<Permission>());
            _sender.Throw = true;

            var act = async () => await Create().ExecuteOrganizationProvisioningAsync(new OrganizationProvisioningEvent { OrganizationId = "org-9", UserId = "creator-1" });

            await act.Should().NotThrowAsync();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Client credentials and OIDC clients
    // ---------------------------------------------------------------------------------------

    public class ClientCredentialNotificationTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly RecordingSender _sender = new();
        private readonly Mock<Blocks.Secrets.ISecretService> _secrets = new();

        public ClientCredentialNotificationTests()
        {
            TestContext.Install(userId: "admin-1");
            _secrets.Setup(s => s.SetAsync(It.IsAny<Blocks.Secrets.SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync("cs-ref");
        }

        public void Dispose() => TestContext.Clear();

        private AuthenticationDomainService Create() =>
            new(new Mock<IMessageClient>().Object, _repo.Object,
                new AuthenticationRequestValidators(
                    new Mock<IValidator<SaveOIDCClientRequest>>().Object,
                    new Mock<IValidator<SaveOidcUiTemplateRequest>>().Object,
                    new Mock<IValidator<SaveIdentityProviderRequest>>().Object,
                    new Mock<IValidator<UpdateIdentityProviderRequest>>().Object),
                new Mock<IHttpClientFactory>().Object,
                TestDoubles.IdentityProviderSecretTestDoubles.Resolver(), TestDoubles.ClientCredentialSecretTestDoubles.Resolver(_secrets),
                NullLogger<AuthenticationDomainService>.Instance, _sender);

        [Fact]
        public async Task Create_NotifiesActor_WithClientIdOnly()
        {
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "runner", Roles = ["admin"] });

            result.IsSuccess.Should().BeTrue();
            var n = _sender.Sent.Single();
            n.ConfigurationName.Should().Be("iam-client-credential-changed");
            n.UserIds.Should().Equal("admin-1");
            var json = RecordingSender.PayloadJson(n);
            json.Should().Contain($"\"clientId\":\"{result.ItemId}\"").And.Contain("\"change\":\"created\"");
            json.Should().NotContain(result.ClientSecret!);
        }

        [Fact]
        public async Task Create_SaveFails_NoNotification()
        {
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = false });

            await Create().SaveClientCredentialAsync(new SaveClientCredentialRequest { Name = "runner", Roles = ["admin"] });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Rotate_NotifiesActorAndCreator_WithoutSecret()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(new ClientCredential
            {
                ItemId = "cc-1", OrganizationId = "default", CreatedBy = "owner-1", ClientSecret = "old"
            });
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            var n = _sender.Sent.Single();
            n.UserIds.Should().BeEquivalentTo("admin-1", "owner-1");
            var json = RecordingSender.PayloadJson(n);
            json.Should().Contain("secret-rotated").And.Contain("\"clientId\":\"cc-1\"");
            json.Should().NotContain(result.ClientSecret!).And.NotContain("old");
        }

        [Fact]
        public async Task Delete_Notifies_AndNotifierFailureDoesNotFailDelete()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-2")).ReturnsAsync(new ClientCredential { ItemId = "cc-2", OrganizationId = "default", CreatedBy = "admin-1" });
            _repo.Setup(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>())).Returns(Task.CompletedTask);
            _sender.Throw = true;

            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-2" });

            result.IsSuccess.Should().BeTrue();
            _sender.Sent.Should().ContainSingle(n => n.ConfigurationName == "iam-client-credential-changed");
        }

        [Fact]
        public async Task Delete_NotFound_NoNotification()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-x")).ReturnsAsync((ClientCredential)null!);

            await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-x" });

            _sender.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task OidcRotate_Notifies_WithPublicClientIdOnly()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("reg-1")).ReturnsAsync(new OidcClientRegistration
            {
                ItemId = "reg-1", ClientId = "public-client", ClientSecret = "old-secret"
            });

            var result = await Create().RotateOidcClientSecretAsync("reg-1");

            var n = _sender.Sent.Single();
            var json = RecordingSender.PayloadJson(n);
            json.Should().Contain("\"clientId\":\"public-client\"").And.Contain("\"kind\":\"oidc-client\"");
            json.Should().NotContain(result.ClientSecret!).And.NotContain("old-secret");
        }

        [Fact]
        public async Task OidcDelete_Notifies_WhenClientExisted()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("reg-2")).ReturnsAsync(new OidcClientRegistration { ItemId = "reg-2", ClientId = "c2" });
            _repo.Setup(r => r.GetIdentityProvidersAsync()).ReturnsAsync(new List<IdentityProvider>());

            await Create().DeleteOidcClientAsync(new DeleteOIDCClientRequest { ItemId = "reg-2" });

            _sender.Sent.Should().ContainSingle(n => RecordingSender.PayloadJson(n).Contains("\"change\":\"deleted\""));
        }
    }
}
