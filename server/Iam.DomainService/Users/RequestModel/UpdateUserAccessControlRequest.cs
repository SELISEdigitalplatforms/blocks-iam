namespace Iam.DomainService.Users
{
    public class UpdateUserAccessControlRequest
    {
        public required string UserId { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public string? OrganizationId { get; set; }

        // Mails the user only when this call actually adds them to the organization; editing the
        // roles of an existing member sends nothing.
        public bool NotifyUser { get; set; } = true;
    }

    public class RevokeUserAccessControlRequest
    {
        public required string UserId { get; set; }
        public string? OrganizationId { get; set; }

        // Mails the user only when they actually belonged to the organization.
        public bool NotifyUser { get; set; } = true;
    }

}
