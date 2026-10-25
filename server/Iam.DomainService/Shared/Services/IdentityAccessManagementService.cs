using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Iam.DomainService.Services
{
    public class IdentityAccessManagementService : IIdentityAccessManagementService
    {
        private const string DefaultMailLanguage = "en-US";

        private readonly ILogger<IdentityAccessManagementService> _logger;
        private readonly ITenants _tenants;
        private readonly IMessageClient _messageClient;
        private readonly IUserRepository _userRepository;

        public IdentityAccessManagementService(
            ILogger<IdentityAccessManagementService> logger,
            ITenants tenants,
            ICryptoService cryptoService,
            IMessageClient messageClient,
            IUserRepository userRepository
        )
        {
            _logger = logger;
            _tenants = tenants;
            _messageClient = messageClient;
            _userRepository = userRepository;
        }

        public string HashPassword(string password, string? optionalSalt = null)
        {
            var passwordMaterial = BuildPasswordMaterial(password, optionalSalt);
            return BCryptNet.HashPassword(passwordMaterial);
        }

        public bool VerifyPassword(string password, string passwordHash, string? optionalSalt = null)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                return false;
            }

            var passwordMaterial = BuildPasswordMaterial(password, optionalSalt);
            try
            {
                return BCryptNet.Verify(passwordMaterial, passwordHash);
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                _logger.LogWarning(ex, "Password hash is not a valid BCrypt hash format.");
                return false;
            }
        }

        private static string BuildPasswordMaterial(string password, string? optionalSalt)
        {
            return string.IsNullOrWhiteSpace(optionalSalt)
                ? password
                : $"{password}::{optionalSalt}";
        }

        public async Task SendToQueueAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public async Task SendToTopicAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToMassConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public async Task<bool> SendEmailAsync(SendMail sendMailCommand)
        {
            await SendToQueueAsync(IdpConstants.MailQueue, sendMailCommand);

            return true;
        }

        public async Task<bool> SendActivationToEmailAsync(User user, string accountActivationUri, string emailPurpose)
        {
            _logger.LogInformation("Sending Activation for {Id} by email: {MPurpose}", user.ItemId, emailPurpose);

            // The mail service drops a mail with no recipient without telling anyone, so it is
            // never queued and the caller hears that nothing was sent.
            var to = NormalizeEmail(user.Email);
            if (to.Length == 0)
            {
                _logger.LogWarning("Activation for {Id} not queued: the account has no email address", user.ItemId);
                return false;
            }

            // A resend reuses the purpose stored on the user, which predates the template rename.
            if (emailPurpose == IdpConstants.LegacyProjectInvitationMailPurpose)
            {
                emailPurpose = IdpConstants.ProjectInvitationMailPurpose;
            }

            var bodyContext = await GetBodyContextAsync(user, emailPurpose, accountActivationUri);

            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = bodyContext,
                // A blank language is rejected by the mail service, so it falls back like null does.
                Language = string.IsNullOrWhiteSpace(user.Language) ? DefaultMailLanguage : user.Language,
                Purpose = emailPurpose,
                To = new string[] { to }
            };

            await SendToQueueAsync(IdpConstants.MailQueue, sendMailCommand);

            return true;
        }

        private async Task<Dictionary<string, string>> GetBodyContextAsync(User user, string mailPurpose, string link)
        {

            if (mailPurpose == IdpConstants.ProjectInvitationMailPurpose)
            {
                var projectId = await _userRepository.GetProjectIdFromProjectPeopleAsync(user.ItemId);
                // The template needs the key; an unknown project still sends the link.
                var projectName = string.IsNullOrWhiteSpace(projectId)
                    ? string.Empty
                    : _tenants.GetTenantByID(projectId)?.Name ?? string.Empty;

                return new Dictionary<string, string>
                {
                    { "ProjectInvitationLink", link },
                    { "DisplayName", string.IsNullOrWhiteSpace(user.FirstName) ? user.Email : $"{user.FirstName} {user.LastName}" },
                    { "ProjectName", projectName }
                };
            }

            return new Dictionary<string, string> { { "AccountActivationUrl" , link}, { "UserName" , user.UserName}, { "DisplayName" , $"{user.FirstName} {user.LastName}"}};
        }


        public async Task<bool> SendAccountActivationEmailAsync(User user, string mailPurpose)
        {
            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                {
                    { "UserName" , user.UserName},
                    { "DisplayName" , $"{user.FirstName} {user.LastName}"},

                    { "CreatedUser.Salutation" , user.Salutation},
                    { "CreatedUser.FirstName" , user.FirstName},
                    { "CreatedUser.LastName" , user.LastName},
                    { "CreatedUser.Email" , user.Email},
                },
                Language = user.Language ?? DefaultMailLanguage,
                Purpose = string.IsNullOrWhiteSpace(mailPurpose) ? "AccountActivated" : mailPurpose,
                To = new string[] { NormalizeEmail(user.Email) }
            };

            return await SendEmailAsync(sendMailCommand);
        }

        public bool IsRoot()
        {
            var bc = BlocksContext.GetContext();
            return _tenants.GetTenantByID(bc.TenantId)?.IsRootTenant ?? false;
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }

    }
}
