namespace Iam.DomainService.Dtos
{
    public class PropagationRolePermissionUpdateEvent
    {
        public string Entity { get; set; } // "role" or "permission"
        public string Action { get; set; } // "update" or "insert"
        public required string ItemId { get; set; } // role id or permission id

        /// <summary>
        /// Whether the administrator explicitly consented to revoking this from users who still
        /// hold it.
        /// </summary>
        /// <remarks>
        /// Propagation is asynchronous, so consent captured in the request has to travel with the
        /// message -- the consumer has no other way to know a human agreed to it. Defaults to
        /// false, which is what a message serialised before this field existed deserialises to, so
        /// an old or replayed message can only ever be less destructive than intended, never more.
        /// </remarks>
        public bool ConfirmRevokeFromUsers { get; set; }
    }
}
