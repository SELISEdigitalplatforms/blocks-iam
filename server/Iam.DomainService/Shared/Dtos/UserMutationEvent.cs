using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public record UserMutationEvent
    {
        public required string ItemId { get; set; }
        public required MutationEventType Action { get; set; }

        /// <summary>
        /// OIDC context of the application the user was created from, carried across the
        /// queue so the activation email can link back to it. Nullable by design: portal
        /// invites have no such context, and messages already in flight during a deploy
        /// deserialize with these unset and fall back to the tenant's default client.
        /// </summary>
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
    }
}
