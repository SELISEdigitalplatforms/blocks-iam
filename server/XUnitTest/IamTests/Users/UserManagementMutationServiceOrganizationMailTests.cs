using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
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
    /// The "added to / removed from organization" mails. They go out only when membership actually
    /// changes, only when <c>NotifyUser</c> is set (the default), and a failure to send never fails
    /// the change itself.
    /// </summary>
    public sealed class UserManagementMutationServiceOrganizationMailTests : IDisposable
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

        private readonly List<SendMail> _sentMails = new();

        public UserManagementMutationServiceOrganizationMailTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _resourceRepo.Setup(r => r.GetOrganizationById("org-x"))
                .ReturnsAsync(new Organization { ItemId = "org-x", Name = "Org X" });
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback<SendMail>(_sentMails.Add)
                .ReturnsAsync(true);
        }

        private static void InstallContext(
            bool isAuthenticated = true,
            string organizationId = "default",
            string? displayName = "Admin Person")
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: isAuthenticated, requestUri: "https://test", organizationId: organizationId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: displayName, oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object, _myAccountValidator.Object,
                _iam.Object, _userRepo.Object, _message.Object, _cache.Object, _tenants.Object, _activity.Object,
                null, _resourceRepo.Object, null);

        private static User OutsiderUser() => new()
        {
            ItemId = "target-1",
            Email = "Target@Example.com",
            FirstName = "Tara",
            LastName = "Get",
            OrganizationIds = new List<string> { "default" }
        };

        private static User MemberUser() => new()
        {
            ItemId = "target-1",
            Email = "target@example.com",
            OrganizationIds = new List<string> { "default", "org-x" },
            Roles = new() { { "org-x", new List<string> { "admin" } } }
        };

        // ─── users/access ─────────────────────────────────────────────────────

        [Fact]
        public async Task Access_NewMember_SendsAddedMail()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            var mail = _sentMails.Should().ContainSingle().Subject;
            mail.Purpose.Should().Be(IdpConstants.OrganizationMemberAddedMailPurpose);
            mail.To.Should().Equal("target@example.com");
            mail.BodyDataContext["OrganizationName"].Should().Be("Org X");
            mail.BodyDataContext["DisplayName"].Should().Be("Tara Get");
            mail.BodyDataContext["ChangedBy"].Should().Be("Admin Person");
            mail.SubjectDataContext["OrganizationName"].Should().Be("Org X");
        }

        [Fact]
        public async Task Access_ExistingMemberRoleEdit_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = "org-x", Roles = new List<string> { "viewer" }
            });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Access_NotifyUserFalse_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            var result = await Create().UpdateUserAccessControlAsync(new UpdateUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = "org-x", NotifyUser = false
            });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Access_MailFailure_DoesNotFailTheGrant()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new InvalidOperationException("queue down"));

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _userRepo.Verify(r => r.UpdateUserAsync(It.Is<User>(u => u.OrganizationIds.Contains("org-x"))), Times.Once);
        }

        [Fact]
        public async Task Access_RepositoryFailure_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        // ─── users/revoke-access ──────────────────────────────────────────────

        [Fact]
        public async Task Revoke_Member_SendsRemovedMail()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            var mail = _sentMails.Should().ContainSingle().Subject;
            mail.Purpose.Should().Be(IdpConstants.OrganizationMemberRemovedMailPurpose);
            mail.To.Should().Equal("target@example.com");
            mail.BodyDataContext["OrganizationName"].Should().Be("Org X");
        }

        [Fact]
        public async Task Revoke_NotAMember_SucceedsWithoutMail()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Revoke_NotifyUserFalse_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());

            var result = await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = "org-x", NotifyUser = false
            });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Revoke_MailFailure_DoesNotFailTheRevoke()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new InvalidOperationException("queue down"));

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
        }

        // ─── users/create for an email that already has an account ────────────

        [Fact]
        public async Task Create_ExistingEmailNewOrganization_SendsAddedMail()
        {
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(OutsiderUser());

            var result = await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().ContainSingle()
                .Which.Purpose.Should().Be(IdpConstants.OrganizationMemberAddedMailPurpose);
        }

        [Fact]
        public async Task Create_ExistingEmailNotifyUserFalse_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(OutsiderUser());

            await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x", NotifyUser = false });

            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ExistingEmailAnonymousCaller_SendsNothing()
        {
            // Signup and queue consumers reach this path without an identity.
            InstallContext(isAuthenticated: false);
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(OutsiderUser());

            await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x" });

            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ExistingEmailAlreadyMember_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(MemberUser());

            await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x" });

            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ExistingEmailSendMailFalse_StillSendsAddedMail()
        {
            // SendMail governs the activation mail of a brand-new account only; the membership
            // mail answers to NotifyUser alone.
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(OutsiderUser());

            var result = await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x", SendMail = false });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().ContainSingle()
                .Which.Purpose.Should().Be(IdpConstants.OrganizationMemberAddedMailPurpose);
            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()), Times.Never);
        }

        [Fact]
        public async Task Create_NewEmailNotifyUserFalse_StillRequestsActivationMail()
        {
            _userRepo.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await Create().CreateUserAsync(
                new CreateUserRequest { Email = "brand-new@example.com", OrganizationId = "org-x", NotifyUser = false });

            result.IsSuccess.Should().BeTrue();
            _message.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<UserMutationEvent>>(c => c.Payload.SendMail)), Times.Once);
        }

        [Fact]
        public async Task Create_NewEmail_SendsNoMembershipMail()
        {
            // A brand-new account is greeted by its activation mail; an "added to organization"
            // mail on top of it would be a second message for the same invite.
            _userRepo.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await Create().CreateUserAsync(
                new CreateUserRequest { Email = "brand-new@example.com", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_ExistingEmailCallerFromOtherOrganization_RefusedWithoutMail()
        {
            InstallContext(organizationId: "org-y");
            _userRepo.Setup(r => r.GetUserByEmailAsync("target@example.com")).ReturnsAsync(OutsiderUser());

            var result = await Create().CreateUserAsync(
                new CreateUserRequest { Email = "target@example.com", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        // ─── users/access: further cases ─────────────────────────────────────

        [Fact]
        public async Task Access_MemberThroughPermissionsKeyOnly_SendsNothing()
        {
            // Membership is read three ways; a Permissions key alone already makes them a member.
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(new User
            {
                ItemId = "target-1",
                Email = "target@example.com",
                OrganizationIds = new List<string> { "default" },
                Permissions = new() { { "org-x", new List<string> { "read" } } }
            });

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Access_DefaultOrganization_NamesItDefaultWithoutLookup()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(new User
            {
                ItemId = "target-1",
                Email = "target@example.com"
            });

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "default" });

            result.IsSuccess.Should().BeTrue();
            var mail = _sentMails.Should().ContainSingle().Subject;
            mail.BodyDataContext["OrganizationId"].Should().Be("default");
            mail.BodyDataContext["OrganizationName"].Should().Be("Default");
            _resourceRepo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Access_UserWithoutEmail_GrantsWithoutMail()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(new User
            {
                ItemId = "target-1",
                Email = string.Empty,
                OrganizationIds = new List<string> { "default" }
            });

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
        }

        [Fact]
        public async Task Access_OrganizationNotFound_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-missing" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Access_CallerFromOtherOrganization_SendsNothing()
        {
            InstallContext(organizationId: "org-y");
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            var result = await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Access_MailGoesOutOnlyAfterTheGrantIsSaved()
        {
            var order = new List<string>();
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>()))
                .Callback(() => order.Add("save")).ReturnsAsync(true);
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback(() => order.Add("mail")).ReturnsAsync(true);

            await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            order.Should().Equal("save", "mail");
        }

        [Fact]
        public async Task Access_MailContext_UsesUserLanguageTenantNameAndEmailFallback()
        {
            _tenants.Setup(t => t.GetTenantByID("tenant-1")).Returns(new Tenant
            {
                TenantId = "tenant-1",
                Name = "Acme",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    PublicCertificatePath = "certs/pub.pem",
                    IssueDate = DateTime.UtcNow
                }
            });
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(new User
            {
                ItemId = "target-1",
                Email = "Nameless@Example.com",
                Language = "de-DE",
                OrganizationIds = new List<string> { "default" }
            });

            await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            var mail = _sentMails.Should().ContainSingle().Subject;
            mail.Language.Should().Be("de-DE");
            mail.To.Should().Equal("nameless@example.com");
            mail.BodyDataContext["DisplayName"].Should().Be("Nameless@Example.com");
            mail.BodyDataContext["ApplicationName"].Should().Be("Acme");
            mail.BodyDataContext["OrganizationId"].Should().Be("org-x");
        }

        [Fact]
        public async Task Access_ActorWithoutDisplayName_ChangedByFallsBackToUserName()
        {
            InstallContext(displayName: null);
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(OutsiderUser());

            await Create().UpdateUserAccessControlAsync(
                new UpdateUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            _sentMails.Should().ContainSingle()
                .Which.BodyDataContext["ChangedBy"].Should().Be("tester");
        }

        // ─── users/revoke-access: further cases ──────────────────────────────

        [Fact]
        public async Task Revoke_MemberThroughRolesKeyOnly_SendsRemovedMail()
        {
            // Granted through a Roles key without ever reaching OrganizationIds: still a member.
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(new User
            {
                ItemId = "target-1",
                Email = "target@example.com",
                OrganizationIds = new List<string> { "default" },
                Roles = new() { { "org-x", new List<string> { "admin" } } }
            });

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeTrue();
            _sentMails.Should().ContainSingle()
                .Which.Purpose.Should().Be(IdpConstants.OrganizationMemberRemovedMailPurpose);
        }

        [Fact]
        public async Task Revoke_RepositoryFailure_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Revoke_OrganizationNotFound_SendsNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-missing" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Revoke_OwnAccess_RefusedWithoutMail()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("actor-1")).ReturnsAsync(new User
            {
                ItemId = "actor-1",
                Email = "a@b.com",
                OrganizationIds = new List<string> { "default", "org-x" }
            });

            var result = await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "actor-1", OrganizationId = "org-x" });

            result.IsSuccess.Should().BeFalse();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Revoke_NotifyUserFalse_StillRecordsEventAndActivity()
        {
            // Opting out of the mail must not opt out of the audit trail.
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());

            await Create().RevokeUserAccessControlAsync(new RevokeUserAccessControlRequest
            {
                UserId = "target-1", OrganizationId = "org-x", NotifyUser = false
            });

            _sentMails.Should().BeEmpty();
            _message.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<UserMutationEvent>>(c => c.Payload.Action == MutationEventType.Update)), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(
                It.Is<UserActivityEvent>(e => e.Event == "USER_ACCESS_REVOKED")), Times.Once);
        }

        [Fact]
        public async Task Revoke_MailGoesOutOnlyAfterTheRevokeIsSaved()
        {
            var order = new List<string>();
            _userRepo.Setup(r => r.GetUserByIdAsync("target-1")).ReturnsAsync(MemberUser());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>()))
                .Callback(() => order.Add("save")).ReturnsAsync(true);
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback(() => order.Add("mail")).ReturnsAsync(true);

            await Create().RevokeUserAccessControlAsync(
                new RevokeUserAccessControlRequest { UserId = "target-1", OrganizationId = "org-x" });

            order.Should().Equal("save", "mail");
        }

        // ─── wire format ─────────────────────────────────────────────────────

        [Fact]
        public void NotifyUser_AbsentFromJson_DefaultsToTrue()
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

            System.Text.Json.JsonSerializer.Deserialize<UpdateUserAccessControlRequest>(
                """{"userId":"u-1","organizationId":"org-x"}""", options)!.NotifyUser.Should().BeTrue();
            System.Text.Json.JsonSerializer.Deserialize<RevokeUserAccessControlRequest>(
                """{"userId":"u-1","organizationId":"org-x"}""", options)!.NotifyUser.Should().BeTrue();
            System.Text.Json.JsonSerializer.Deserialize<CreateUserRequest>(
                """{"email":"a@b.com"}""", options)!.NotifyUser.Should().BeTrue();
        }

        [Fact]
        public void NotifyUser_FalseInJson_IsHonoured()
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

            System.Text.Json.JsonSerializer.Deserialize<UpdateUserAccessControlRequest>(
                """{"userId":"u-1","notifyUser":false}""", options)!.NotifyUser.Should().BeFalse();
            System.Text.Json.JsonSerializer.Deserialize<RevokeUserAccessControlRequest>(
                """{"userId":"u-1","notifyUser":false}""", options)!.NotifyUser.Should().BeFalse();
            System.Text.Json.JsonSerializer.Deserialize<CreateUserRequest>(
                """{"email":"a@b.com","notifyUser":false}""", options)!.NotifyUser.Should().BeFalse();
        }
    }
}
