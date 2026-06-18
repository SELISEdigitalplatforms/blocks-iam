using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class Organization : BaseEntity
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public string? ParentOrganizationId { get; set; }
        public string? ShortCode { get; set; }
        public bool IsDisabled { get; set; } = false;
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForMembers { get; set; } = new List<string>();

        // Contact
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<Address> Addresses { get; set; } = new List<Address>();

        // Branding
        public Theme? Theme { get; set; }
        public string? LogoUrl { get; set; }
        public string? LogoId { get; set; }
        public string? Industry { get; set; }
        public string TimeZone { get; set; } = "UTC";
        public string? Currency { get; set; }
        public string DateFormat { get; set; } = "yyyy-MM-dd";
        public string TimeFormat { get; set; } = "HH:mm";
        public string Locale { get; set; } = "en-US";

        // Add custom attributes to the organization
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    [BsonIgnoreExtraElements]
    public class Theme
    {
        public string? Name { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? TertiaryColor { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

    [BsonIgnoreExtraElements]
    public class Address
    {
        public string Name { get; set; } = string.Empty;
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public bool IsPrimary { get; set; }
    }
}
