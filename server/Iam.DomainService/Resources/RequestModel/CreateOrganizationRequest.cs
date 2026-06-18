using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public class CreateOrganizationRequest
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForMembers { get; set; } = new List<string>();
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<Address> Addresses { get; set; } = new List<Address>();
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();

        public CreatedFrom CreatedFrom { get; set; } = 0;
    }

    public enum CreatedFrom
    {
        Cloud = 1,
        ConstructSignup = 2,
        ConstructPortal = 3,
    }
}
