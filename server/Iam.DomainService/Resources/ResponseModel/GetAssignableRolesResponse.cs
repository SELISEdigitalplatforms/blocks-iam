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

        /// <summary>Lets a picker draw the tree without a second call.</summary>
        public string? ParentRoleSlug { get; set; }

        public bool CanCreateOwn { get; set; }

        /// <summary>No parent and may create roles below it: its holders reach every member of the organization.</summary>
        public bool IsTopRole { get; set; }
    }
}
