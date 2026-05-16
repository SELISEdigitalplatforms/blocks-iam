using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public class CreateUserViaSsoRequest
    {
        public string? UserId { get; set; }
        public string? Language { get; set; } = "en-US";
        public required string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MailPurpose { get; set; }
        public bool SendWelcomeMail { get; set; } = true;
        public UserCreationType UserCreationType { get; set; } = UserCreationType.Social;
        public required string Platform { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public List<UserLogInType> AllowedLogInType { get; set; } = new List<UserLogInType> { UserLogInType.SSO };
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public bool Active { get; set; } = true;
        public bool IsVerified { get; set; } = true;
        public string? ExternalUserId { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(); // For any additional info from SSO provider that doesn't fit into existing properties
        public string? OrganizationId { get; set; }
    }
}
