namespace Authentication.DomainService.Security.Contracts
{
    public sealed class RevokeSessionRequest
    {
        public string? Reason { get; set; }
    }
}