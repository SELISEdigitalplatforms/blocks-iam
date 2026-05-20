namespace Iam.DomainService.Resources
{
    public class AssignRolesToOrganizationRequest
    {
        public string OrganizationId { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
    }
}
