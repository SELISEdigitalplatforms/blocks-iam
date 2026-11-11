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

    /// <summary>How an issuance ends once organization status is taken into account (spec #668 §6.1).</summary>
    public enum OrganizationIssuanceOutcome
    {
        /// <summary>A token may be issued for <see cref="OrganizationIssuanceDecision.Scope"/>.</summary>
        Issue,

        /// <summary>403 <c>organization_disabled</c>: no token, code or approval is created.</summary>
        OrganizationDisabled,

        /// <summary>503 <c>temporarily_unavailable</c>: the status could not be read, so nothing is issued.</summary>
        StatusUnavailable
    }

    /// <param name="Outcome">Whether the issuance may go ahead.</param>
    /// <param name="Scope">The scope to mint when <see cref="Outcome"/> is <see cref="OrganizationIssuanceOutcome.Issue"/>.</param>
    /// <param name="OrganizationStatus">
    /// <c>"disabled"</c> only for the <c>"no-org"</c> token a user whose organizations are all disabled gets while
    /// the tenant allows it; null otherwise. Becomes the <c>org_status</c> claim and response field.
    /// </param>
    public sealed record OrganizationIssuanceDecision(
        OrganizationIssuanceOutcome Outcome,
        OrganizationScope Scope,
        string? OrganizationStatus = null)
    {
        public bool IsIssued => Outcome == OrganizationIssuanceOutcome.Issue;
    }

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

        /// <summary>The <c>org_status</c> value of a "no-org" token issued because every membership is disabled.</summary>
        public const string DisabledOrganizationStatus = "disabled";

        /// <summary>
        /// The ids one status lookup has to cover for <see cref="ResolveIssuance"/>: the requested organization, the
        /// last-used one and every membership (organization ids, then role keys, then permission keys). Blanks and
        /// the sentinels are left out, because <c>"default"</c> and <c>"no-org"</c> always count as active.
        /// Empty means no lookup is needed.
        /// </summary>
        public static IReadOnlyList<string> CollectStatusCandidates(User user, string? requestedOrganizationId)
        {
            ArgumentNullException.ThrowIfNull(user);

            return new[] { requestedOrganizationId, user.LastUsedOrganizationId }
                .Concat(Memberships(user))
                .Where(id => !string.IsNullOrWhiteSpace(id) && !IsReservedOrganizationId(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The issuance decision under multi-org once organization status is known (spec #668 §6.1, rules 3-7).
        /// The caller handles multi-org off (always <c>"default"</c>, no lookup) and a failed lookup (503).
        /// </summary>
        /// <param name="user">The user the token is being minted for.</param>
        /// <param name="requestedOrganizationId">The organization the request names, if any.</param>
        /// <param name="isExplicit">
        /// The requested organization was carried in explicitly (client-sent, refresh hint, switch target,
        /// token-exchange grant, link): a disabled membership is refused. Otherwise the server filled it in, and it
        /// is only a preference: honoured while it is an active membership, else the fallback applies.
        /// </param>
        /// <param name="isLinkOrganization">
        /// The explicit organization comes from a signup link or link session. Links keep their own handling;
        /// only the status check is added: a disabled or missing link organization is refused.
        /// </param>
        /// <param name="organizationStatuses">Id -> IsDisabled for the ids that have an organization document.</param>
        /// <param name="allowNoOrgSignIn">The tenant's <c>AllowNoOrgSignInForDisabledOrganizations</c>.</param>
        /// <param name="hasAccess">The membership test, normally <c>OrganizationAccessResolver.HasOrganizationAccess</c>.</param>
        public static OrganizationIssuanceDecision ResolveIssuance(
            User user,
            string? requestedOrganizationId,
            bool isExplicit,
            bool isLinkOrganization,
            IReadOnlyDictionary<string, bool> organizationStatuses,
            bool allowNoOrgSignIn,
            Func<User, string?, bool> hasAccess)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(organizationStatuses);
            ArgumentNullException.ThrowIfNull(hasAccess);

            // Rule 3: active means the document exists and is not disabled; a missing document is not active.
            bool IsActive(string? organizationId) =>
                IsReservedOrganizationId(organizationId)
                || (organizationId is not null
                    && organizationStatuses.TryGetValue(organizationId, out var isDisabled)
                    && !isDisabled);

            var isRequestedMembership = !string.IsNullOrWhiteSpace(requestedOrganizationId)
                && !string.Equals(requestedOrganizationId, IdpConstants.NoOrganizationId, StringComparison.Ordinal)
                && hasAccess(user, requestedOrganizationId);

            if (isExplicit && isLinkOrganization && !string.IsNullOrWhiteSpace(requestedOrganizationId) && !IsActive(requestedOrganizationId))
            {
                return Refused(OrganizationIssuanceOutcome.OrganizationDisabled);
            }

            // Rule 4: an explicit organization the user belongs to is that organization or a refusal, never a
            // fallback. One the user does not belong to is ignored, as before; "no-org" is never honoured.
            // A server-filled preference is honoured only while active; otherwise rule 5 decides.
            if (isRequestedMembership)
            {
                if (IsActive(requestedOrganizationId))
                {
                    return Issued(Organization(requestedOrganizationId!));
                }

                if (isExplicit)
                {
                    return Refused(OrganizationIssuanceOutcome.OrganizationDisabled);
                }
            }

            // Rule 5: last used if it is still a membership and active, else the first active membership in the
            // order the sign-in path has always used.
            if (hasAccess(user, user.LastUsedOrganizationId) && IsActive(user.LastUsedOrganizationId))
            {
                return Issued(Organization(user.LastUsedOrganizationId!));
            }

            var memberships = Memberships(user).ToList();
            var firstActive = memberships.FirstOrDefault(IsActive);
            if (firstActive is not null)
            {
                return Issued(Organization(firstActive));
            }

            // Rule 6: a member of nothing keeps today's "no-org", with no status, whatever the setting.
            if (memberships.Count == 0)
            {
                return Issued(NoOrganizationScope);
            }

            // Rule 7: memberships exist, but none is active.
            return allowNoOrgSignIn
                ? Issued(NoOrganizationScope, DisabledOrganizationStatus)
                : Refused(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        /// <summary>The decision for an issuance whose organization was not status-checked (multi-org off, impersonation).</summary>
        public static OrganizationIssuanceDecision Issued(OrganizationScope scope, string? organizationStatus = null) =>
            new(OrganizationIssuanceOutcome.Issue, scope, organizationStatus);

        /// <summary>The scope of an organization stored at authorize or approval, minted as it was stored.</summary>
        public static OrganizationScope ForStoredOrganization(string? organizationId) =>
            string.IsNullOrWhiteSpace(organizationId) || string.Equals(organizationId, IdpConstants.NoOrganizationId, StringComparison.Ordinal)
                ? NoOrganizationScope
                : Organization(organizationId);

        /// <summary>A refusal; the scope is never minted.</summary>
        public static OrganizationIssuanceDecision Refused(OrganizationIssuanceOutcome outcome) =>
            new(outcome, NoOrganizationScope);

        private static IEnumerable<string> Memberships(User user) =>
            (user.OrganizationIds ?? [])
                .Concat(user.Roles?.Keys ?? Enumerable.Empty<string>())
                .Concat(user.Permissions?.Keys ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id));

        // A "default" reached through a membership is the tenant-wide scope, whichever rule found it.
        private static OrganizationScope Organization(string organizationId) =>
            string.Equals(organizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal)
                ? TenantWideScope
                : new(OrganizationScopeKind.Organization, organizationId);

        private static string? FirstNonBlank(IEnumerable<string>? values) =>
            values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
