namespace Authentication.DomainService.Shared.RequestModel
{
    public class ImpersonateRequest
    {
        public string TargetTenantId { get; set; }
        public string? OrganizationId { get; set; }
    }

    public class ImpersonateResponse
    {
        public bool impersonation_mode { get; set; } = true;
        public bool org_switched { get; set; } = false;
    }

    public class StopImpersonationResponse
    {
        public bool impersonation_mode { get; set; } = false;
    }
}
