using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public class UpdateUserRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? Tags { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public UserMfaType UserMfaType { get; set; }
        public bool MfaEnabled { get; set; }
        public Dictionary<string, List<string>> Roles { get; set; } = new();
        public Dictionary<string, List<string>> Permissions { get; set; } = new();
        public string? ProjectKey { get; set; }
    }
}
