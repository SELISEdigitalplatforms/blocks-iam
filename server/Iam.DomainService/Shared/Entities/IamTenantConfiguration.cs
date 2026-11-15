using Blocks.Genesis.Entities.Iam;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Shared.Entities
{
    /// <summary>
    /// The tenant configuration as blocks-iam reads and writes it: the shared Genesis
    /// <see cref="TenantConfiguration"/> plus the settings only blocks-iam uses.
    /// </summary>
    /// <remarks>
    /// Same document as the Genesis entity (the <c>TenantConfiguration</c> field of <c>IdentityConfigurations</c>,
    /// or the old <c>TenantConfigurations</c> collection); <c>TenantConfigurationStore</c> always reads into this
    /// type. The Genesis entity ignores these extra elements, so other readers of the document are unaffected.
    /// </remarks>
    [BsonIgnoreExtraElements]
    public class IamTenantConfiguration : TenantConfiguration
    {
        /// <summary>
        /// Whether every new sign-up (email or SSO first login) joins <see cref="SignupAutoJoinOrganizationId"/>.
        /// Only takes effect while multi-org is on. A document written before this field existed reads as false,
        /// so no migration is needed.
        /// </summary>
        public bool IsSignupAutoJoinEnabled { get; set; }

        /// <summary>
        /// The organization new sign-ups join while <see cref="IsSignupAutoJoinEnabled"/> is on: <c>"default"</c> or
        /// an organization ItemId. Kept when auto-join is switched off. Never exposed on the anonymous config read.
        /// </summary>
        public string? SignupAutoJoinOrganizationId { get; set; }

        /// <summary>
        /// What a user whose organizations are all disabled (or missing) gets at sign-in while multi-org is on:
        /// <c>false</c> refuses the token with 403 <c>organization_disabled</c>; <c>true</c> issues a <c>"no-org"</c>
        /// token with no roles or permissions and <c>org_status = "disabled"</c>, so apps can show their own
        /// disabled state. Ignored while multi-org is off. A document written before this field existed reads as
        /// false, so no migration is needed. Only exposed on the admin config read.
        /// </summary>
        public bool AllowNoOrgSignInForDisabledOrganizations { get; set; }
    }
}
