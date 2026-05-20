using Blocks.Genesis;

namespace Iam.DomainService.Entities
{
    public class UserOrganizationMembership : BaseEntity
    {
        public string TenantId { get; set; }
        public string UserId { get; set; }
        public string OrganizationId { get; set; }
        public string Status { get; set; } = "active";  // active, suspended, inactive
        public DateTime JoinedDate { get; set; }
    }
}
