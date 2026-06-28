namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetAssignableRolesResponse
    {
        public List<AssignableRole> Hierarchy { get; set; } = new();
        public List<AssignableRole> Standalone { get; set; } = new();
    }

    public class AssignableRole
    {
        public required string Slug { get; set; }
        public required string Name { get; set; }

    }
}
