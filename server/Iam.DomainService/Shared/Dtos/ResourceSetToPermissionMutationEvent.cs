using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class ResourceSetToPermissionMutationEvent
    {
        public required List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public required string Slug { get; set; }
        public required ResourceEntity Entity { get; set; }
        public string OrganizationId { get; set; }

        /// <summary>
        /// Whether the caller asked for this change to reach every organization's copy of the role.
        /// </summary>
        /// <remarks>
        /// Carried on the message because the propagation runs asynchronously on IamPermissionQueue
        /// and the consumer has no other way to know what the request asked for. Defaults to false,
        /// which is also what a message serialised before this field existed deserialises to.
        /// </remarks>
        public bool PropagateToAllOrganizations { get; set; }
    }
}
