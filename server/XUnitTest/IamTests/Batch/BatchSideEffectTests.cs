using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Actions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// The users/create side effects the batch reuses for the users it wrote (spec 683 §6.5 step 8): the create
    /// event with the item's sendEmail (H8), the user created action, and the member-added mail (H9).
    /// </summary>
    public sealed class BatchSideEffectTests : IDisposable
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IActionPublisher> _actions = new();
        private readonly List<UserMutationEvent> _events = [];
        private readonly List<(string Name, string Data)> _published = [];
        private readonly List<SendMail> _mails = [];

        public BatchSideEffectTests()
        {
            BlocksContext.IsTestMode = true;
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()))
                .Callback<ConsumerMessage<UserMutationEvent>>(m =>
                {
                    m.ConsumerName.Should().Be(IdpConstants.IamUserQueue);
                    _events.Add(m.Payload);
                })
                .Returns(Task.CompletedTask);
            _actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) => _published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);
            _iam.Setup(i => i.SendEmailAsync(It.IsAny<SendMail>())).Callback<SendMail>(_mails.Add).ReturnsAsync(true);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null!);
            BlocksContext.IsTestMode = false;
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance,
                new Mock<IValidator<CreateUserRequest>>().Object,
                new Mock<IValidator<UpdateUserRequest>>().Object,
                new Mock<IValidator<UpdateMyAccountRequest>>().Object,
                _iam.Object, new Mock<IUserRepository>().Object, _message.Object, new Mock<ICacheClient>().Object,
                new Mock<ITenants>().Object, new Mock<IUserActivityDispatcher>().Object,
                actionPublisher: _actions.Object);

        private static User NewUser() => new()
        {
            ItemId = "u-1",
            Email = "a@example.com",
            UserCreationType = UserCreationType.Api,
            OrganizationIds = ["org-a", "org-b"],
            Roles = new() { ["org-a"] = ["viewer"], ["org-b"] = ["editor"] },
            Permissions = new() { ["org-a"] = ["docs.read"] }
        };

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CreatedUser_PublishesTheCreateEventWithTheResolvedSendMail_H8(bool sendMail)
        {
            await Create().PublishBatchUserCreatedAsync(NewUser(), "org-a", sendMail, "caller-1");

            var created = _events.Should().ContainSingle().Subject;
            created.ItemId.Should().Be("u-1");
            created.Action.Should().Be(MutationEventType.Create);
            created.SendMail.Should().Be(sendMail);
            created.InvitedBy.Should().Be("caller-1");
            created.OrganizationId.Should().Be("org-a");

            var action = _published.Should().ContainSingle().Subject;
            action.Name.Should().Be(IamActionNames.UserCreated);
            action.Data.Should().Contain("\"roles\":[\"viewer\"]").And.Contain("\"permissions\":[\"docs.read\"]").And.Contain("\"creationType\":\"Api\"");
            action.Data.Should().NotContain("a@example.com");
        }

        [Fact]
        public async Task ExistingUserAddedToAnOrganization_GetsTheMemberAddedMailForThatOrganization_H9()
        {
            await Create().NotifyOrganizationMemberAddedAsync(NewUser(), "default");

            var mail = _mails.Should().ContainSingle().Subject;
            mail.Purpose.Should().Be(IdpConstants.OrganizationMemberAddedMailPurpose);
            mail.To.Should().Equal("a@example.com");
            mail.BodyDataContext["OrganizationId"].Should().Be("default");
        }

        [Fact]
        public async Task NullUser_IsRejected()
        {
            var service = Create();

            await FluentActions.Awaiting(() => service.PublishBatchUserCreatedAsync(null!, "o", true, null)).Should().ThrowAsync<ArgumentNullException>();
            await FluentActions.Awaiting(() => service.NotifyOrganizationMemberAddedAsync(null!, "o")).Should().ThrowAsync<ArgumentNullException>();
        }
    }
}
