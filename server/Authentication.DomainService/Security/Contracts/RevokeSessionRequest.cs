namespace Authentication.DomainService.Security.Contracts
{
    public sealed class RevokeSessionRequest
    {
        public string? Reason { get; set; }

        // Optional: blank means the caller. Nullable so MVC does not treat it as required.
        public string? UserId { get; set; }
    }
}