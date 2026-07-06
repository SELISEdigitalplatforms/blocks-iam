namespace Iam.DomainService.Resources
{
    public class UpdateRoleRequest
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string? ParentRoleSlug { get; set; }
        public bool PropagateToOtherOrg { get; set; }
        public bool CanCreateOwn { get; set; }
    }
}
