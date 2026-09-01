using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public class UpdateUserRequest
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
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public string? OrganizationId { get; set; }

        /// <summary>
        /// Free-form extras. Tri-state: omitted (null) leaves the stored bag untouched, an empty
        /// object clears it, and a populated object replaces it wholesale. There is no per-key
        /// merge, so a client sends the complete bag it wants persisted.
        /// </summary>
        public Dictionary<string, object>? Attributes { get; set; }
    }
}
