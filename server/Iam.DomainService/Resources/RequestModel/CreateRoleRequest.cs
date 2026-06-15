namespace Iam.DomainService.Resources
{
    public class CreateRoleRequest
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public string Slug { get; set; }
        public string? ParentRoleSlug { get; set; }
        public bool PropagateToOtherOrg { get; set; } = false;
        public bool CanCreateOwn { get; set; } = false;
    }
}
