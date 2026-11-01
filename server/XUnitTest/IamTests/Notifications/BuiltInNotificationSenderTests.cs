using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using FluentAssertions;
using Iam.DomainService.Notifications;
using Moq;

namespace XUnitTest.IamTests.Notifications
{
    public class BuiltInNotificationSenderTests : IDisposable
    {
        private const string TenantId = "tenant-1";

        private readonly Mock<INotificationPublisher> _publisher = new();
        private readonly List<NotificationRequest> _published = new();

        public BuiltInNotificationSenderTests()
        {
            _publisher.Setup(p => p.Publish(It.IsAny<NotificationRequest>()))
                .Callback<NotificationRequest>(_published.Add);
        }

        public void Dispose() => TestContext.Clear();

        private BuiltInNotificationSender Create() => new(_publisher.Object);

        private static BuiltInNotification Notification(string? organizationId = null, params string[] userIds) =>
            new(TenantId, BuiltInNotificationNames.UserDeactivated, userIds,
                BuiltInNotificationSender.Payload(BuiltInNotificationNames.UserDeactivated, "t", "d",
                    fields: new Dictionary<string, object?> { ["userId"] = "u1" }),
                organizationId);

        private static void InstallImpersonated(string rootUserId, string orgId)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: rootUserId, impersonated: true,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "root", phoneNumber: null, displayName: "R", oauthToken: null,
                originalTenantId: "root-tenant", impersonationSessionId: null, applicationDomain: "test"));
        }

        [Fact]
        public async Task Send_Publishes_NameTenantOrganizationPayloadAndCleanUsers()
        {
            var ok = await Create().SendAsync(Notification("org-1", "u1", "u1", " ", ""));

            ok.Should().BeTrue();
            var request = _published.Should().ContainSingle().Subject;
            request.Name.Should().Be("iam-user-deactivated");
            request.TenantId.Should().Be(TenantId);
            request.OrganizationId.Should().Be("org-1");
            request.NamedUserIds.Should().Equal("u1");
            request.Payload.Should().NotBeNull();
        }

        [Fact]
        public async Task Send_WithoutOrganization_UsesContextOrganization()
        {
            TestContext.Install(orgId: "ctx-org");

            await Create().SendAsync(Notification(null, "u1"));

            _published.Single().OrganizationId.Should().Be("ctx-org");
        }

        [Fact]
        public async Task Send_ImpersonatedRootUser_AddsActingUserAsNamedUser()
        {
            InstallImpersonated("root-user", "org-1");

            await Create().SendAsync(Notification("org-1", "u1"));

            _published.Single().NamedUserIds.Should().Equal("u1", "root-user");
        }

        [Fact]
        public async Task Send_ImpersonatedRootUser_NoticeAboutAnotherAccount_DoesNotAddActingUser()
        {
            InstallImpersonated("root-user", "org-1");

            await Create().SendAsync(Notification("org-1", "u1") with { IncludeImpersonatedActor = false });

            _published.Single().NamedUserIds.Should().Equal("u1");
        }

        [Fact]
        public async Task Send_NotImpersonated_DoesNotAddActingUser()
        {
            TestContext.Install(userId: "actor-1");

            await Create().SendAsync(Notification("org-1", "u1"));

            _published.Single().NamedUserIds.Should().Equal("u1");
        }

        [Fact]
        public async Task Send_NoName_PublishesNothing()
        {
            var ok = await Create().SendAsync(Notification("org-1", "u1") with { ConfigurationName = " " });

            ok.Should().BeFalse();
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task TrySend_NullSender_OrThrowingSender_DoesNotThrow()
        {
            IBuiltInNotificationSender? none = null;
            await none.TrySendAsync(Notification("org-1", "u1"));

            var throwing = new Mock<IBuiltInNotificationSender>();
            throwing.Setup(s => s.SendAsync(It.IsAny<BuiltInNotification>())).ThrowsAsync(new InvalidOperationException());
            var act = async () => await throwing.Object.TrySendAsync(Notification("org-1", "u1"));
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void Payload_SkipsNullFields_AndKeepsCommonShape()
        {
            var payload = BuiltInNotificationSender.Payload("cfg", "title", "desc", true,
                new Dictionary<string, object?> { ["a"] = "1", ["b"] = null });

            payload.Keys.Should().BeEquivalentTo("event", "title", "description", "isSuccess", "a", "occurredAt");
        }
    }
}
