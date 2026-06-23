using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public class SaveOrganizationRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForMembers { get; set; } = new List<string>();
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<Address> Addresses { get; set; } = new List<Address>();
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
        public Theme? Theme { get; set; }
        public string? LogoUrl { get; set; }
        public string? LogoId { get; set; }
        public string? Industry { get; set; }
        public string? TimeZone { get; set; }
        public string? Currency { get; set; }
        public string? DateFormat { get; set; }
        public string? TimeFormat { get; set; }
        public string? Locale { get; set; }
        public bool? IsEnable { get; set; }
    }
}
