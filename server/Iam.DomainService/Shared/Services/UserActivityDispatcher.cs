using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Services
{
    public class UserActivityDispatcher : IUserActivityDispatcher
    {
        private readonly IMessageClient _messageClient;
        private readonly ILogger<UserActivityDispatcher> _logger;

        public UserActivityDispatcher(IMessageClient messageClient, ILogger<UserActivityDispatcher> logger)
        {
            _messageClient = messageClient;
            _logger = logger;
        }

        public async Task SendUserActivityAsync(UserActivityEvent evt)
        {
            if (evt is null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (string.IsNullOrEmpty(evt.MessageId))
            {
                evt = evt with { MessageId = Guid.NewGuid().ToString() };
            }

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<UserActivityEvent>
            {
                ConsumerName = IdpConstants.UserActivityQueue,
                Payload = evt
            });
        }
    }
}