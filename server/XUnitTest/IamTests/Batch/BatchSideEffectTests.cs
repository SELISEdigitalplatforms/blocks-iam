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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// The users/create side effect the batch reuses for the users it created: the user created action, with the
    /// roles and permissions of the given organization. No create event and no mail: the batch writes the timeline
    /// entry itself and sends mails in its mail phase.
    /// </summary>
    public sealed class BatchSideEffectTests : IDisposable
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IActionPublisher> _actions = new();
        private readonly List<(string Name, string Data)> _published = [];

        public BatchSideEffectTests()
        {
            BlocksContext.IsTestMode = true;
            _actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) => _published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);
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

        [Fact]
        public async Task CreatedUser_PublishesOnlyTheUserCreatedAction_ForTheGivenOrganization()
        {
            await Create().PublishBatchUserCreatedActionAsync(NewUser(), "org-a");

            var action = _published.Should().ContainSingle().Subject;
            action.Name.Should().Be(IamActionNames.UserCreated);
            action.Data.Should().Contain("\"roles\":[\"viewer\"]").And.Contain("\"permissions\":[\"docs.read\"]").And.Contain("\"creationType\":\"Api\"");
            action.Data.Should().NotContain("a@example.com").And.NotContain("editor");

            _message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>()), Times.Never);
            _iam.Verify(i => i.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
        }

        [Fact]
        public async Task NullUser_IsRejected()
        {
            var service = Create();

            await FluentActions.Awaiting(() => service.PublishBatchUserCreatedActionAsync(null!, "o")).Should().ThrowAsync<ArgumentNullException>();
        }
    }
}
