namespace Iam.DomainService.Resources
{
    public class AssignPermissionsToOrganizationRequest
    {
        public string OrganizationId { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new List<string>();
        public List<string> Groups { get; set; } = new List<string>();
    }
}
