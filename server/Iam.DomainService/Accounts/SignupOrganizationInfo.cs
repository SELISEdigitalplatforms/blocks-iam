using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Organization profile a caller may supply when creating an organization during signup.
    /// <para>
    /// This type is an allowlist, and deliberately not a subset of
    /// <c>CreateOrganizationRequest</c>. Signup is <c>[AllowAnonymous]</c>, so binding the
    /// request straight onto <c>CreateOrganizationRequest</c> would expose
    /// <c>DefaultRoleForMembers</c> / <c>DefaultPermissionsForMembers</c> — and
    /// <c>UserManagementMutationService.CreateUserAsync</c> falls back to those org defaults
    /// whenever the create request carries no explicit roles, which is exactly what signup does
    /// on any tenant with an empty <c>DefaultRolesForNewUserOnSignUp</c>. Posting
    /// <c>"defaultRoleForMembers": ["admin"]</c> would then make the signing-up user, and every
    /// later member of that organization, an admin.
    /// </para>
    /// <para>
    /// Also excluded: <c>ParentOrganizationId</c> (grafting into another organization's
    /// hierarchy), <c>IsDisabled</c> (lifecycle control), <c>LogoId</c> (a storage identifier
    /// that must come from the upload service) and <c>ShortCode</c> (no uniqueness policy yet).
    /// Fields are mapped across one by one in <c>AccountService</c>, so widening
    /// <c>CreateOrganizationRequest</c> later cannot silently widen this anonymous surface.
    /// </para>
    /// </summary>
    public class SignupOrganizationInfo
    {
        public string? Name { get; set; }
        public string? Description { get; set; }

        // Contact
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<Address> Addresses { get; set; } = new List<Address>();

        // Branding and localisation. Left null, each falls back to the Organization entity
        // default (TimeZone "UTC", DateFormat "yyyy-MM-dd", TimeFormat "HH:mm", Locale "en-US").
        public Theme? Theme { get; set; }
        public string? LogoUrl { get; set; }
        public string? Industry { get; set; }
        public string? TimeZone { get; set; }
        public string? Currency { get; set; }
        public string? DateFormat { get; set; }
        public string? TimeFormat { get; set; }
        public string? Locale { get; set; }

        /// <summary>
        /// Free-form extras. Never persisted as received — see <see cref="Iam.DomainService.Shared.Serialization.AttributeNormalizer"/>.
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }
}
