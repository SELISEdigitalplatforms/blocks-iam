namespace Iam.DomainService.Resources
{
    public class CreateRoleRequest
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public string Slug { get; set; }
        public string? ParentRoleSlug { get; set; }
        public bool CanCreateOwn { get; set; } = false;

        /// <summary>
        /// The caller's acknowledgement that another organization already has a role with this
        /// name, and that creating this one will give them a second role sharing it.
        /// </summary>
        /// <remarks>
        /// Same shape as <c>confirmRevokeFromUsers</c> on the archive endpoints: without it the
        /// create is refused and the counts are reported; with it the create proceeds. Inert when
        /// no other organization holds the name, and ignored entirely for a non-default caller,
        /// which is never shown the advisory.
        /// </remarks>
        public bool ConfirmDuplicateName { get; set; } = false;
    }
}
