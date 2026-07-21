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

        public CreatedFrom CreatedFrom { get; set; } = CreatedFrom.Cloud;
    }

    /// <summary>
/// Source surface that initiated creation of an organisation. Used for
/// billing attribution and for the "Where did this org come from?" report.
/// </summary>
public enum CreatedFrom
{
    /// <summary>Sentinel/unknown value. Reserved; never set by the platform.</summary>
    Cloud = 1,

    /// <summary>Created from the public self-service signup flow.</summary>
    ConstructSignup = 2,

    /// <summary>Created by an administrator from the Construct portal.</summary>
    ConstructPortal = 3,
}
}
