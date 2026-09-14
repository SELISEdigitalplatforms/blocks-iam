using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public class CreateOrganizationRequest
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForMembers { get; set; } = new List<string>();
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<Address> Addresses { get; set; } = new List<Address>();
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();

        // Branding and localisation. Null or blank leaves the Organization entity default in
        // place (TimeZone "UTC", DateFormat "yyyy-MM-dd", TimeFormat "HH:mm", Locale "en-US")
        // rather than writing an empty string over it.
        public Theme? Theme { get; set; }
        public string? LogoUrl { get; set; }
        public string? Industry { get; set; }
        public string? TimeZone { get; set; }
        public string? Currency { get; set; }
        public string? DateFormat { get; set; }
        public string? TimeFormat { get; set; }
        public string? Locale { get; set; }

        public CreatedFrom CreatedFrom { get; set; } = CreatedFrom.Cloud;
    }

    /// <summary>
/// Source surface that initiated creation of an organisation.
/// </summary>
/// <remarks>
/// Each value selects which <c>TenantConfiguration.AllowOrgCreationFrom*</c> flag gates the
/// request, and <see cref="ConstructPortal"/> additionally decides whether the caller is added
/// to the organization they just created. The three flags are independent, so changing the value
/// a client sends also changes which flag must be enabled for it to work at all.
/// <para>
/// Not persisted: <c>Organization</c> has no field for it. It survives only on the
/// <c>ORGANIZATION_CREATED</c> activity event, so any "where did this org come from?" reporting
/// has to read it from there rather than from the organization document.
/// </para>
/// <para>
/// Bound from the wire as a number -- no string-enum converter is configured -- so clients send
/// <c>1</c>, <c>2</c> or <c>3</c>, and an omitted value binds to <see cref="Cloud"/>.
/// </para>
/// </remarks>
public enum CreatedFrom
{
    /// <summary>
    /// Created from the blocks-os portal, which sends this value explicitly. Gated by
    /// <c>AllowOrgCreationFromCloud</c>. The caller is not added to the new organization.
    /// Also the value an omitted <c>createdFrom</c> binds to.
    /// </summary>
    Cloud = 1,

    /// <summary>
    /// Created from the public self-service signup flow. Set by the signup and SSO provisioning
    /// paths themselves rather than by a client. Gated by <c>AllowOrgCreationFromSignup</c>;
    /// those paths attach the new user to the organization as part of creating them.
    /// </summary>
    ConstructSignup = 2,

    /// <summary>
    /// Created by an administrator from the Construct portal. Gated by
    /// <c>AllowOrgCreationFromPortal</c>. The caller is added to the new organization as its
    /// first member, unless the request carries an impersonated token.
    /// </summary>
    ConstructPortal = 3,
}
}
