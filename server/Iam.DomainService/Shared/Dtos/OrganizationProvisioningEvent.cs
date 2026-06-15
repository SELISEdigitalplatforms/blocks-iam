namespace Iam.DomainService.Dtos
{
    public class OrganizationProvisioningEvent
    {
        public string OrganizationId { get; set; } = default!;
        public string UserId { get; set; } = default!;
    }
}
