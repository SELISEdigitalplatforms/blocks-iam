namespace Iam.DomainService.Resources
{
    public class AssignPermissionsToOrganizationRequest
    {
        public string OrganizationId { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new List<string>();
        public bool IsCarryRoles { get; set; } = false; // Indicates whether to also assign the permissions to the roles in the organization
    }
}
