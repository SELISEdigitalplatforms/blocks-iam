using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Services
{
    public interface IIdentityAccessManagementService
    {
        string HashPassword(string password, string? optionalSalt = null);
        bool VerifyPassword(string password, string passwordHash, string? optionalSalt = null);
        Task SendToQueueAsync<T>(string queue, T payload) where T : class;
        Task SendToTopicAsync<T>(string queue, T payload) where T : class;
        Task<bool> SendEmailAsync(SendMail sendMailCommand);
        Task<bool> SendActivationToEmailAsync(User user, string accountActivationUri, string emailPurpose);
        Task<bool> SendAccountActivationEmailAsync(User user, string mailPurpose);
        bool IsRoot();
    }
}
