using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests
{
    public class IdentityAccessManagementServiceTests : IDisposable
    {
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<IUserRepository> _userRepository = new();

        public IdentityAccessManagementServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private IdentityAccessManagementService Create() =>
            new(NullLogger<IdentityAccessManagementService>.Instance, _tenants.Object, _crypto.Object, _messageClient.Object, _userRepository.Object);

        private static Tenant MakeTenant(string id, string? name = null, bool isRoot = false) => new()
        {
            TenantId = id,
            Name = name!,
            IsRootTenant = isRoot,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
        };

        // ---------- HashPassword / VerifyPassword ----------

        [Fact]
        public void HashPassword_ProducesVerifiableBcryptHash()
        {
            var svc = Create();
            var hash = svc.HashPassword("Secret123!");

            hash.Should().NotBeNullOrWhiteSpace();
            hash.Should().StartWith("$2");
            svc.VerifyPassword("Secret123!", hash).Should().BeTrue();
        }

        [Fact]
        public void VerifyPassword_WrongPassword_ReturnsFalse()
        {
            var svc = Create();
            var hash = svc.HashPassword("Secret123!");

            svc.VerifyPassword("wrong", hash).Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_EmptyHash_ReturnsFalse()
        {
            Create().VerifyPassword("anything", "").Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_InvalidHashFormat_ReturnsFalse()
        {
            // Not a valid BCrypt hash -> SaltParseException is caught and false returned.
            Create().VerifyPassword("anything", "not-a-bcrypt-hash").Should().BeFalse();
        }

        [Fact]
        public void HashPassword_WithSalt_ChangesMaterial()
        {
            var svc = Create();
            var saltedHash = svc.HashPassword("Secret123!", "salt-A");

            // The same password with the matching salt verifies.
            svc.VerifyPassword("Secret123!", saltedHash, "salt-A").Should().BeTrue();
            // Without the salt (or with a different salt) verification fails.
            svc.VerifyPassword("Secret123!", saltedHash).Should().BeFalse();
            svc.VerifyPassword("Secret123!", saltedHash, "salt-B").Should().BeFalse();
        }

        // ---------- SendToQueueAsync / SendToTopicAsync ----------

        [Fact]
        public async Task SendToQueueAsync_SendsConsumerMessageWithQueueAndPayload()
        {
            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var payload = new SendMail { Purpose = "p" };
            await Create().SendToQueueAsync("queue-x", payload);

            captured.Should().NotBeNull();
            captured!.ConsumerName.Should().Be("queue-x");
            captured.Payload.Should().BeSameAs(payload);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Once);
        }

        [Fact]
        public async Task SendToTopicAsync_SendsMassConsumerMessage()
        {
            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var payload = new SendMail { Purpose = "topic" };
            await Create().SendToTopicAsync("topic-x", payload);

            captured.Should().NotBeNull();
            captured!.ConsumerName.Should().Be("topic-x");
            captured.Payload.Should().BeSameAs(payload);
            _messageClient.Verify(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Once);
        }

        // ---------- SendEmailAsync ----------

        [Fact]
        public async Task SendEmailAsync_QueuesMailAndReturnsTrue()
        {
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(Task.CompletedTask);

            var result = await Create().SendEmailAsync(new SendMail { Purpose = "x" });

            result.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Once);
        }

        // ---------- SendActivationToEmailAsync ----------

        [Fact]
        public async Task SendActivationToEmail_DefaultPurpose_BuildsAccountActivationContext()
        {
            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "u1", Email = "USER@X.com", UserName = "user1", FirstName = "First", LastName = "Last", Language = "de-DE" };
            var result = await Create().SendActivationToEmailAsync(user, "https://activate", "AccountActivation");

            result.Should().BeTrue();
            captured.Should().NotBeNull();
            var mail = captured!.Payload;
            mail.Purpose.Should().Be("AccountActivation");
            mail.Language.Should().Be("de-DE");
            mail.To.Should().ContainSingle().Which.Should().Be("user@x.com"); // lower-cased
            mail.BodyDataContext.Should().ContainKey("AccountActivationUrl").WhoseValue.Should().Be("https://activate");
            mail.BodyDataContext["UserName"].Should().Be("user1");
            mail.BodyDataContext["DisplayName"].Should().Be("First Last");
        }

        [Fact]
        public async Task SendActivationToEmail_ProjectInvitation_BuildsProjectContext()
        {
            _userRepository.Setup(r => r.GetProjectIdFromProjectPeopleAsync("u2")).ReturnsAsync("project-77");
            _tenants.Setup(t => t.GetTenantByID("project-77")).Returns(MakeTenant("project-77", "My Project"));

            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "u2", Email = "inv@x.com", UserName = "inv", FirstName = "Jane", LastName = "Doe" };
            var result = await Create().SendActivationToEmailAsync(user, "https://project-link", "project_invitation");

            result.Should().BeTrue();
            captured.Should().NotBeNull();
            var body = captured!.Payload.BodyDataContext;
            body["ProjectInvitationLink"].Should().Be("https://project-link");
            body["ProjectName"].Should().Be("My Project");
            body["DisplayName"].Should().Be("Jane Doe");
            _userRepository.Verify(r => r.GetProjectIdFromProjectPeopleAsync("u2"), Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmail_ProjectInvitation_UsesEmailWhenFirstNameMissing()
        {
            _userRepository.Setup(r => r.GetProjectIdFromProjectPeopleAsync("u3")).ReturnsAsync("project-1");
            _tenants.Setup(t => t.GetTenantByID("project-1")).Returns(MakeTenant("project-1", "P1"));

            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "u3", Email = "noname@x.com", UserName = "nn", FirstName = null, LastName = null };
            await Create().SendActivationToEmailAsync(user, "https://l", "project_invitation");

            captured!.Payload.BodyDataContext["DisplayName"].Should().Be("noname@x.com");
        }

        // ---------- SendAccountActivationEmailAsync ----------

        [Fact]
        public async Task SendAccountActivationEmail_EmptyPurpose_DefaultsToAccountActivated()
        {
            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "u4", Email = "Acc@X.com", UserName = "acc", Salutation = "Mr", FirstName = "A", LastName = "B" };
            var result = await Create().SendAccountActivationEmailAsync(user, "");

            result.Should().BeTrue();
            captured!.Payload.Purpose.Should().Be("AccountActivated");
            captured.Payload.To.Should().ContainSingle().Which.Should().Be("acc@x.com");
            captured.Payload.BodyDataContext["CreatedUser.Salutation"].Should().Be("Mr");
        }

        [Fact]
        public async Task SendAccountActivationEmail_CustomPurpose_UsesIt()
        {
            ConsumerMessage<SendMail>? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "u5", Email = "c@x.com", UserName = "c", Salutation = "Ms", FirstName = "C", LastName = "D" };
            await Create().SendAccountActivationEmailAsync(user, "WelcomeBack");

            captured!.Payload.Purpose.Should().Be("WelcomeBack");
        }

        // ---------- IsRoot ----------

        [Fact]
        public void IsRoot_RootTenant_ReturnsTrue()
        {
            _tenants.Setup(t => t.GetTenantByID("tenant-1")).Returns(MakeTenant("tenant-1", isRoot: true));
            Create().IsRoot().Should().BeTrue();
        }

        [Fact]
        public void IsRoot_NonRootTenant_ReturnsFalse()
        {
            _tenants.Setup(t => t.GetTenantByID("tenant-1")).Returns(MakeTenant("tenant-1", isRoot: false));
            Create().IsRoot().Should().BeFalse();
        }

        [Fact]
        public void IsRoot_TenantNotFound_ReturnsFalse()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            Create().IsRoot().Should().BeFalse();
        }
    }
}
