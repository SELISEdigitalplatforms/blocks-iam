namespace Iam.DomainService.Dtos
{
    public class PropagationRolePermissionUpdateEvent
    {
        public string Entity { get; set; } // "role" or "permission"
        public string Action { get; set; } // "update" or "insert"
        public required string ItemId { get; set; } // role id or permission id
    }
}
