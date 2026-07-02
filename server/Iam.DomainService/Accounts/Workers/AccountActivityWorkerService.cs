using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Accounts
{
    public class AccountActivityWorkerService : IConsumer<AccountActivityEvent>
    {
        private const string ActivateAccountEvent = "Activate_Account";
        private const string ResetPasswordEvent = "Reset_Password";

        private readonly ILogger<AccountActivityWorkerService> _logger;
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly ICacheClient _cacheClient;

        public AccountActivityWorkerService
        (
            ILogger<AccountActivityWorkerService> logger,
            IIdentityAccessManagementRepository repository,
            IIdentityAccessManagementService identityAccessManagementService,
            ICacheClient cacheClient
        )
        {
            _logger = logger;
            _repository = repository;
            _identityAccessManagementService = identityAccessManagementService;
            _cacheClient = cacheClient;
        }

        public async Task Consume(AccountActivityEvent context)
        {
            ArgumentNullException.ThrowIfNull(context);

            await InvalidateActivationCacheAsync(context.UserId, context.Code);

            var user = await _repository.GetUserByIdAsync(context.UserId);

            await SaveUserTimeline(user, context);

            _logger.LogInformation("Event type: {Event}", context.Event);

            if (!context.PreventPostEvent)
            {
                await HandlePostEventAsync(user, context);
            }
        }

        public async Task<bool> SaveUserTimeline(User user, AccountActivityEvent context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var blocksContext = BlocksContext.GetContext();

            var timeline = new UserTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                UserId = user.ItemId,
                OrganizationId = blocksContext.OrganizationId,
                CreatedBy = string.IsNullOrWhiteSpace(blocksContext?.UserId) ? user.CreatedBy : blocksContext.UserId,
                CreatedDate = DateTime.Now,
                CurrentData = user,
                Event = context.Event
            };

            await _repository.InsertUserTimelineAsync(timeline);
            return true;
        }

        public async Task<bool> HandlePostEventForActivation(User user, string mailPurpose)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose);
        }

        public async Task<bool> HandlePostEventForResetPassword(string userId)
        {
            await _identityAccessManagementService.SendToQueueAsync(IdpConstants.AuthenticationQueue, new LogoutAllEvent
            {
                UserId = userId
            });

            return true;
        }

        private async Task InvalidateActivationCacheAsync(string userId, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            var keys = (await _repository.GetActiveUserKeyMapAsync(userId))?.Select(x => x.Key) ?? new List<string>();

            var cacheTask = keys.Select(async x => await _cacheClient.RemoveKeyAsync(x));
            await Task.WhenAll(cacheTask);

            await _repository.UpdateUserKeyMapActivationAsync(userId);
        }

        private async Task HandlePostEventAsync(User user, AccountActivityEvent context)
        {
            switch (context.Event)
            {
                case ActivateAccountEvent:
                    await HandlePostEventForActivation(user, context.MailPurpose);
                    break;
                case ResetPasswordEvent:
                    await HandlePostEventForResetPassword(context.UserId);
                    break;
                default:
                    break;
            }
        }
    }
}
