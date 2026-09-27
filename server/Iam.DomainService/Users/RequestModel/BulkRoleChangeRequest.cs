using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// One bulk role change, expressed as a <em>delta</em> inside a single organization.
    /// <para>
    /// The delta shape is deliberate. <c>iam/users/access</c> replaces a user's whole role list for
    /// an organization, which a bulk caller cannot express without first reading every target's
    /// current roles -- impossible for a filter-shaped target, and a lost-update race even for a
    /// handful of ids. Add/remove is the only shape that survives both.
    /// </para>
    /// <para>
    /// The same body is accepted by the preview and the submit endpoints, so the console sends one
    /// payload twice and the number it showed the operator is the number the worker acts on.
    /// </para>
    /// </summary>
    public class BulkRoleChangeRequest
    {
        /// <summary>
        /// The one organization the delta is applied inside. <c>User.Roles</c> is keyed by
        /// organization, so a role has no meaning without it. When multi-organization is disabled the
        /// caller sends <c>"default"</c>, which the server treats like any other id apart from the
        /// existence check.
        /// <para>
        /// Nullable rather than <c>required</c> so a missing value reaches the validator and comes
        /// back as this feature's own "OrganizationId is required" message, instead of a
        /// deserialization failure the console cannot render.
        /// </para>
        /// </summary>
        public string? OrganizationId { get; set; }

        public List<string> AddRoles { get; set; } = new();

        public List<string> RemoveRoles { get; set; } = new();

        /// <summary>
        /// Who to apply it to. Nullable for the same reason as <see cref="OrganizationId"/>.
        /// </summary>
        public BulkRoleTarget? Target { get; set; }
    }

    /// <summary>
    /// Exactly one of the two forms is supplied. Supplying both, or neither, is a validation error
    /// rather than a precedence rule -- a caller that sends both has two different intents and the
    /// server should not guess which one it meant.
    /// </summary>
    public class BulkRoleTarget
    {
        /// <summary>An explicit, hand-picked set of user ids.</summary>
        public List<string>? UserIds { get; set; }

        /// <summary>
        /// The same DTO the user-list endpoint takes. Reusing it verbatim is what guarantees the set
        /// the operator sees in the console is the set the mutation touches.
        /// </summary>
        public GetUsersFilter? Filter { get; set; }
    }
}
