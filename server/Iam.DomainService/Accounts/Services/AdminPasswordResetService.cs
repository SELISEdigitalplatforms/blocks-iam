using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// An administrator sends a user the password-reset mail. The endpoint already checked the
    /// caller's reset permission, so no captcha is asked (the public /auth/recover keeps it).
    /// Unlike the public path the answers are honest: only an administrator can call this.
    /// The link goes to the user's own address only and is never returned. One mail per user
    /// per minute; a newer code retires the older ones (<see cref="AccountCodes"/>).
    /// </summary>
    public class AdminPasswordResetService : IAdminPasswordResetService
    {
        public static readonly TimeSpan MailInterval = TimeSpan.FromMinutes(1);
        public const string ResetMail = "ResetMail";

        private readonly IUserRepository _userRepository;
        private readonly IAccountService _accountService;
        private readonly ICacheClient _cacheClient;
        private readonly IAccessGrantService _accessGrant;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<AdminPasswordResetService> _logger;

        public AdminPasswordResetService(
            IUserRepository userRepository,
            IAccountService accountService,
            ICacheClient cacheClient,
            IAccessGrantService accessGrant,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<AdminPasswordResetService> logger)
        {
            _userRepository = userRepository;
            _accountService = accountService;
            _cacheClient = cacheClient;
            _accessGrant = accessGrant;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }

        public static string WaitKey(string userId) => $"iam:admin-reset-wait:{userId}";

        public async Task<BaseAccountResponse> SendResetMailAsync(AdminPasswordResetRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.UserId))
            {
                return Failure(nameof(AdminPasswordResetRequest.UserId), "UserId is required");
            }

            var user = await _userRepository.GetUserByIdAsync(request.UserId);
            if (user == null)
            {
                return Failure(nameof(AdminPasswordResetRequest.UserId), "No user found with this id");
            }

            // Same rule as activate and resend activation: the caller may manage this user (R3).
            if (!await _accessGrant.AuthorizeCurrentCallerIfWiredAsync(scope => AccessGrantRules.CheckManageUser(scope, user)))
            {
                return new BaseAccountResponse { Errors = AccessGrantErrors.Create() };
            }

            var refusal = RefusalFor(user);
            if (refusal != null)
            {
                return Failure(nameof(AdminPasswordResetRequest.UserId), refusal);
            }

            if (!await TryHoldMailSlotAsync(user.ItemId))
            {
                return Failure(ResetMail, "A reset mail was sent to this user less than a minute ago. Try again later");
            }

            if (!await SendAsync(user))
            {
                return Failure(ResetMail, "The reset mail could not be sent");
            }

            await RecordAsync(user);
            return new BaseAccountResponse { IsSuccess = true, ItemId = user.ItemId };
        }

        /// <summary>Why this user gets no reset mail, or null when it may be sent.</summary>
        private static string? RefusalFor(User user)
        {
            if (AccountCodes.IsBlocked(user))
            {
                return "The user is blocked. Unblock the user first";
            }

            if (!user.Active)
            {
                return "The user is not activated yet. Send an activation mail instead";
            }

            return string.IsNullOrWhiteSpace(user.Email) ? "The user has no email address" : null;
        }

        /// <summary>SETNX, so two clicks at the same moment still send one mail.</summary>
        private Task<bool> TryHoldMailSlotAsync(string userId) =>
            _cacheClient.CacheDatabase().StringSetAsync(
                key: WaitKey(userId),
                value: "1",
                expiry: MailInterval,
                keepTtl: false,
                when: When.NotExists,
                flags: CommandFlags.None);

        /// <summary>Sends the mail; when it was not queued the slot is given back so the admin can retry at once.</summary>
        private async Task<bool> SendAsync(User user)
        {
            var sent = false;
            try
            {
                sent = await _accountService.ProcessRecoverAccountAsync(user, AccountMailPurposes.RecoverAccount);
                return sent;
            }
            finally
            {
                if (!sent)
                {
                    await _cacheClient.RemoveKeyAsync(WaitKey(user.ItemId));
                }
            }
        }

        private async Task RecordAsync(User user)
        {
            var actorUserId = BlocksContext.GetContext()?.UserId;
            _logger.LogInformation("Admin password reset mail sent to {UserId} by {ActorUserId}", user.ItemId, actorUserId);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = user.ItemId,
                ActorUserId = actorUserId,
                Category = UserActivityCategory.Account,
                Event = "PASSWORD_RESET_SENT_BY_ADMIN",
                Source = "iam-admin-password-reset",
                Entity = "User",
                EntityId = user.ItemId
            });
        }

        private static BaseAccountResponse Failure(string field, string message) =>
            new() { Errors = new Dictionary<string, string> { { field, message } } };
    }
}
