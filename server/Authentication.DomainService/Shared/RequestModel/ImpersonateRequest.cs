namespace Authentication.DomainService.Shared.RequestModel
{
    public class ImpersonateRequest
    {
        public string TargetTenantId { get; set; }
        public string? OrganizationId { get; set; }
    }
}
