using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class PermissionMutationForTenantsEvent
    {
        public required string ItemId { get; set; }
        public required MutationEventType Action { get; set; }
    }
}
