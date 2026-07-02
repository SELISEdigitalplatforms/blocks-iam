namespace Mfa.DomainService.Shared
{
    public class DisableUserMfaRequest
    {
        public string? UserId { get; set; }

        public string? AdminActorUserId { get; set; }

        public string? Reason { get; set; }
    }
}
