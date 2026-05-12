using Blocks.Genesis;
using Iam.DomainService.Shared.Dtos;


namespace Worker.Consumers.Users
{
    public class UserStatusChangedConsumer : IConsumer<UserStatusChangedEvent>
    {
        public UserStatusChangedConsumer()
        {
        }

        public async Task Consume(UserStatusChangedEvent context)
        {
            await Task.CompletedTask;
        }
    }
}
