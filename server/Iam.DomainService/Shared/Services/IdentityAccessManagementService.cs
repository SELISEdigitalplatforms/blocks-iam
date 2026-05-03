using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Iam.DomainService.Services
{
    public class IdentityAccessManagementService : IIdentityAccessManagementService
    {
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
            return BCryptNet.Verify(passwordMaterial, passwordHash);
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
            await SendToQueueAsync(Constants.MailQueue, sendMailCommand);

            return true;
        }

        public async Task<bool> SendActivationToEmailAsync(User user, string accountActivationUri, string emailPurpose, string projectKey)
        {
            _logger.LogInformation("Sending Activation for {Id} by email: {MPurpose}", user.ItemId, emailPurpose);

            var bodyContext = await GetBodyContextAsync(user, emailPurpose, accountActivationUri);

            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = bodyContext,
                Language = user.Language ?? "en-US",
                Purpose = emailPurpose,
                To = [user.Email.ToLower()],
                ProjectKey = projectKey
            };

            await SendToQueueAsync(Constants.MailQueue, sendMailCommand);

            return true;
        }

        private async Task<Dictionary<string, string>> GetBodyContextAsync(User user, string mailPurpose, string link)
        {

            if (mailPurpose == "project_invitation")
            {
                var projectId = await _userRepository.GetProjectIdFromProjectPeopleAsync(user.ItemId);
                var projectName = _tenants.GetTenantByID(projectId)?.Name;

                return new Dictionary<string, string>
                {
                    { "ProjectInvitationLink", link },
                    { "DisplayName", string.IsNullOrWhiteSpace(user.FirstName) ? user.Email : $"{user.FirstName} {user.LastName}" },
                    { "ProjectName", projectName }
                };
            }

            return new Dictionary<string, string> { { "AccountActivationUrl" , link}, { "UserName" , user.UserName}, { "DisplayName" , $"{user.FirstName} {user.LastName}"}};
        }


        public async Task<bool> SendAccountActivationEmailAsync(User user, string mailPurpose, string projectKey)
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
                Language = user.Language ?? "en-US",
                Purpose = string.IsNullOrWhiteSpace(mailPurpose) ? "AccountActivated" : mailPurpose,
                To = new string[] { user.Email.ToLower() },
                ProjectKey = projectKey
            };

            return await SendEmailAsync(sendMailCommand);
        }

        public bool IsRoot()
        {
            var bc = BlocksContext.GetContext();
            return _tenants.GetTenantByID(bc.TenantId)?.IsRootTenant ?? false;
        }

    }
}
