namespace Iam.DomainService.Users.RequestModel
{
    public class ActivateAndLinkSocialIdentityRequest
    {
        public required string UserId { get; set; }
        public required string Provider { get; set; }
        public required string ProviderUserId { get; set; }
        public string? Issuer { get; set; }
    }
}
