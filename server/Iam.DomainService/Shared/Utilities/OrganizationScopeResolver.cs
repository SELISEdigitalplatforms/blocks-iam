using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Utilities
{
    /// <summary>Which of the three organization scopes a token is minted for.</summary>
    public enum OrganizationScopeKind
    {
        /// <summary>The tenant-wide scope: the project has no organizations, or the caller was explicitly granted it.</summary>
        TenantWide,

        /// <summary>One organization, named by <see cref="OrganizationScope.ClaimValue"/>.</summary>
        Organization,

        /// <summary>Authenticated, but a member of no organization.</summary>
        None
    }

    /// <param name="Kind">Which of the three outcomes applies.</param>
    /// <param name="ClaimValue">
    /// The value of the organization claim. Never null, never empty, never whitespace:
    /// <c>"default"</c> for <see cref="OrganizationScopeKind.TenantWide"/>, the organization id for
    /// <see cref="OrganizationScopeKind.Organization"/>, and <c>"no-org"</c> for
    /// <see cref="OrganizationScopeKind.None"/>.
    /// </param>
    public sealed record OrganizationScope(OrganizationScopeKind Kind, string ClaimValue);

    /// <summary>
    /// The one place that decides which organization a token is scoped to.
    /// </summary>
    /// <remarks>
    /// Five code paths used to decide this independently, and three of them reached for
    /// <c>"default"</c> whenever they did not know the answer. That is not a harmless default:
    /// <c>"default"</c> is simultaneously "this project has no organizations" and "tenant-wide
    /// root privilege", so an unknown answer silently became the most privileged one. A fourth
    /// path omitted the claim entirely, which is worse still, because a blank organization is
    /// collapsed back to <c>"default"</c> downstream.
    /// <para>
    /// So the rule this type exists to enforce is narrow and absolute: under multi-org,
    /// <see cref="IdpConstants.DefaultOrganizationId"/> is returned <b>only</b> when it is
    /// literally present in the user's own memberships. No rule here may synthesise it. When
    /// nothing can be resolved the answer is <see cref="OrganizationScopeKind.None"/> -- an
    /// explicit "belongs to nothing" -- never a fallback to tenant-wide.
    /// </para>
    /// <para>
    /// Membership is the three-way test owned by <c>OrganizationAccessResolver.HasOrganizationAccess</c>
    /// (organization ids, role keys, or permission keys), because an organization can be granted
    /// through a role or permission assignment without ever being written to
    /// <see cref="User.OrganizationIds"/>. It is passed in rather than duplicated here.
    /// </para>
    /// </remarks>
    public static class OrganizationScopeResolver
    {
        private static readonly OrganizationScope TenantWideScope =
            new(OrganizationScopeKind.TenantWide, IdpConstants.DefaultOrganizationId);

        private static readonly OrganizationScope NoOrganizationScope =
            new(OrganizationScopeKind.None, IdpConstants.NoOrganizationId);

        /// <summary>
        /// True when <paramref name="organizationId"/> is a scope sentinel rather than a real
        /// organization id. <see cref="Resolve"/> never honours <c>"no-org"</c> from a request, and
        /// honours <c>"default"</c> only for a user who literally holds it.
        /// </summary>
        public static bool IsReservedOrganizationId(string? organizationId) =>
            string.Equals(organizationId, IdpConstants.NoOrganizationId, StringComparison.Ordinal)
            || string.Equals(organizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal);

        /// <summary>
        /// Total: every input yields a scope whose <see cref="OrganizationScope.ClaimValue"/> is a
        /// non-empty string. Never throws.
        /// </summary>
        /// <param name="isMultiOrgEnabled">The tenant's multi-organization mode.</param>
        /// <param name="user">The user the token is being minted for.</param>
        /// <param name="requestedOrganizationId">The organization named by the request, if any.</param>
        /// <param name="hasAccess">
        /// The membership test, normally <c>OrganizationAccessResolver.HasOrganizationAccess</c>.
        /// </param>
        public static OrganizationScope Resolve(
            bool isMultiOrgEnabled,
            User user,
            string? requestedOrganizationId,
            Func<User, string?, bool> hasAccess)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(hasAccess);

            // R0. Single-organization projects have exactly one scope, and neither the request nor
            // the user's memberships can change it.
            if (!isMultiOrgEnabled)
            {
                return TenantWideScope;
            }

            // R1/R2. A requested organization is honoured only when the user actually belongs to
            // it. "no-org" is never honoured: it is not a place. "default" is honoured only when the
            // user literally holds it (SPEC10 C3) -- the membership test below is that check, so
            // a payload naming "default" cannot reach the tenant-wide scope without the grant,
            // while a user who has it can switch into it. Discarded ids are ignored rather than
            // rejected, matching UserListOrganizationScope and ResourceWriteOrganizationScope:
            // the caller learns nothing about whether an unauthorised id exists.
            //
            // "default" used to be discarded unconditionally here, so a member could not switch
            // into it: switch-org answered 200 with a token for the last used organization.
            if (!string.Equals(requestedOrganizationId, IdpConstants.NoOrganizationId, StringComparison.Ordinal)
                && hasAccess(user, requestedOrganizationId))
            {
                return Organization(requestedOrganizationId!);
            }

            // R3. Where the user was last seen, if they still belong there. This is what makes an
            // organization switch survive the next sign-in.
            if (hasAccess(user, user.LastUsedOrganizationId))
            {
                return Organization(user.LastUsedOrganizationId!);
            }

            // R4/R5. Nothing requested and nothing remembered: take the first membership, in the
            // same preference order the sign-in path has always used.
            var firstMembership = FirstNonBlank(user.OrganizationIds)
                ?? FirstNonBlank(user.Roles?.Keys)
                ?? FirstNonBlank(user.Permissions?.Keys);

            // R6. A member of nothing. Explicitly so -- not tenant-wide by omission.
            return firstMembership is null ? NoOrganizationScope : Organization(firstMembership);
        }

        // A "default" reached through a membership is the tenant-wide scope, whichever rule found it.
        private static OrganizationScope Organization(string organizationId) =>
            string.Equals(organizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal)
                ? TenantWideScope
                : new(OrganizationScopeKind.Organization, organizationId);

        private static string? FirstNonBlank(IEnumerable<string>? values) =>
            values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
