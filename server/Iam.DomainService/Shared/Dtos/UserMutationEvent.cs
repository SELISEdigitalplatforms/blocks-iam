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

        /// <summary>
        /// Whether a <see cref="MutationEventType.Create"/> sends the activation mail. Defaults to
        /// true so messages already in flight during a deploy keep sending it.
        /// </summary>
        public bool SendMail { get; set; } = true;

        /// <summary>
        /// The signed-in user who sent the invitation and the organization it is for. They travel on the
        /// message so a failed activation mail can be reported to the inviter. Null for self signup and for
        /// messages queued before this existed: then nobody is told.
        /// </summary>
        public string? InvitedBy { get; set; }
        public string? OrganizationId { get; set; }
    }
}
