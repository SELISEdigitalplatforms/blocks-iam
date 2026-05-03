namespace DomainService.OAuth
{
    public sealed class TokenIssuanceContext
    {
        public bool IsImpersonation { get; set; }
        public string? OriginalTenantId { get; set; }
        public string? ActorUserId { get; set; }
    }
}
