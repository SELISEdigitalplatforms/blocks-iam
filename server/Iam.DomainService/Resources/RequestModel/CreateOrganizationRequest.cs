namespace Iam.DomainService.Resources
{
    public class CreateOrganizationRequest
    {
        public string Name { get; set; }
        public string InitializeRolesMode { get; set; } = "Empty";  // "Empty" or "CopySelected"
        public List<string> RoleSlugsToCopy { get; set; } = [];     // Only used if InitializeRolesMode = "CopySelected"
    }
}
