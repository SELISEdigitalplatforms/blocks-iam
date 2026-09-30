namespace Iam.DomainService.Resources
{
    public class UpdateRoleRequest
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }

        /// <summary>
        /// The new parent. <c>null</c> (omitted) leaves the parent as it is; an empty string detaches
        /// the role from its parent. The distinction exists because an edit form that only changes
        /// the name used to send nothing here -- and that silently detached every role it saved.
        /// </summary>
        public string? ParentRoleSlug { get; set; }

        public bool PropagateToOtherOrg { get; set; }

        /// <summary><c>null</c> (omitted) leaves the flag as it is, for the same reason as <see cref="ParentRoleSlug"/>.</summary>
        public bool? CanCreateOwn { get; set; }
    }
}
