using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Resources;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Utilities
{
    /// <summary>The tenant settings an issuance-time organization decision reads.</summary>
    public sealed record OrganizationIssuanceSettings(bool IsMultiOrgEnabled, bool AllowNoOrgSignInForDisabledOrganizations)
    {
        public static readonly OrganizationIssuanceSettings SingleOrganization = new(false, false);
    }

    /// <summary>
    /// Resolves which organization a user should be signed into.
    ///
    /// Both methods are thin adapters over <see cref="OrganizationScopeResolver"/>, which owns the
    /// rules and is the single place they live. This type contributes the membership test
    /// (<see cref="HasOrganizationAccess"/>) and keeps the call shapes the sign-in legs already use.
    ///
    /// Selection rules (see <see cref="OrganizationScopeResolver.Resolve"/> for the authoritative order):
    ///   1. The requested organization, if the user belongs to it. "no-org" is never honoured;
    ///      "default" only when the user literally holds it.
    ///   2. LastUsedOrganizationId, if the user still belongs to it.
    ///   3. The first organization id, falling back to the first role key, then the first
    ///      permission key.
    ///   4. Otherwise "no-org" -- never "default", which under multi-org is the tenant-wide scope
    ///      and must be granted explicitly, never reached by a fallback.
    /// </summary>
    public static class OrganizationAccessResolver
    {
        /// <summary>
        /// The organization an OIDC authorize / device-approval leg should record. Unlike the
        /// sign-in legs below, the value this returns is stored (on the authorization code or the
        /// device approval) and becomes the claim without being re-validated at mint time, so the
        /// caller must pass the real <paramref name="isMultiOrgEnabled"/> rather than relying on a
        /// later correction.
        /// <para>
        /// Returns a non-null, non-empty scope value. It used to return <c>null</c> when the user
        /// belonged to nothing, which made the organization claim disappear from the token
        /// altogether -- and a blank organization is read as <c>"default"</c> downstream.
        /// </para>
        /// </summary>
        public static string ResolveEffectiveOrganizationId(User user, bool isMultiOrgEnabled) =>
            Resolve(user, requestedOrganizationId: null, isMultiOrgEnabled).ClaimValue;

        /// <summary>
        /// The organization an interactive sign-in should be scoped to. Every credential-based leg
        /// (password, social, and the mfa second leg that completes either of them) must agree on
        /// this value: it becomes the organization claim AND the key
        /// <see cref="OAuth.AuthorizationClaimsResolver"/> reads roles and permissions out of, so a
        /// leg that skips it mints a token with no roles for an org-scoped user.
        ///
        /// A caller-supplied organization is honoured only when the user actually belongs to it, so
        /// this is safe to apply to request-controlled input.
        /// </summary>
        /// <param name="isMultiOrgEnabled">
        /// Optional here, and defaulted to <c>true</c>, because these legs only <b>pre-fill</b>
        /// <c>TokenRequest.OrganizationId</c>: the authoritative decision is taken again inside
        /// <see cref="OAuth.JwtAccessTokenProvider.GetJwtAccessToken"/>, which reads the tenant's
        /// real mode. A single-organization tenant therefore still mints <c>"default"</c> even if
        /// this pre-fill said otherwise.
        /// </param>
        public static string ResolveSignInOrganizationId(
            User user,
            string? requestedOrganizationId,
            bool isMultiOrgEnabled = true) =>
            Resolve(user, requestedOrganizationId, isMultiOrgEnabled).ClaimValue;

        /// <summary>
        /// The full scope decision, when the caller needs the <see cref="OrganizationScopeKind"/>
        /// and not just the claim value.
        /// </summary>
        public static OrganizationScope Resolve(User user, string? requestedOrganizationId, bool isMultiOrgEnabled) =>
            OrganizationScopeResolver.Resolve(isMultiOrgEnabled, user, requestedOrganizationId, HasOrganizationAccess);

        /// <summary>
        /// The tenant settings an issuance decision needs, from the one tenant-configuration read every mint
        /// already does. A configuration that cannot be read is treated as multi-org disabled (the existing rule),
        /// so the all-disabled setting does not apply either.
        /// </summary>
        public static async Task<OrganizationIssuanceSettings> ReadIssuanceSettingsAsync(
            IResourceRepository resourceRepository,
            ILogger logger,
            string? userId)
        {
            try
            {
                var tenantConfiguration = await resourceRepository.GetTenantConfigurationAsync();
                return new OrganizationIssuanceSettings(
                    tenantConfiguration?.IsMultiOrgEnabled ?? false,
                    tenantConfiguration?.AllowNoOrgSignInForDisabledOrganizations ?? false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the tenant configuration while minting a token for user {UserId}; treating multi-organization mode as disabled.", userId);
                return OrganizationIssuanceSettings.SingleOrganization;
            }
        }

        /// <summary>
        /// The issuance-time organization decision with organization status (spec #668 §6.1): multi-org off is
        /// always <c>"default"</c> with no lookup; otherwise one status read covers every candidate id, and a
        /// failed read refuses with <see cref="OrganizationIssuanceOutcome.StatusUnavailable"/> (fail closed).
        /// </summary>
        /// <param name="requestedOrganizationId">The organization the request names, if any.</param>
        /// <param name="isExplicit">See <see cref="OrganizationScopeResolver.ResolveIssuance"/>: explicit or a server-filled preference.</param>
        /// <param name="isLinkOrganization">The requested organization comes from a signup link or link session.</param>
        public static async Task<OrganizationIssuanceDecision> ResolveForIssuanceAsync(
            IResourceRepository resourceRepository,
            ILogger logger,
            User user,
            string? requestedOrganizationId,
            bool isExplicit,
            bool isLinkOrganization,
            OrganizationIssuanceSettings settings)
        {
            if (!settings.IsMultiOrgEnabled)
            {
                return OrganizationScopeResolver.Issued(Resolve(user, requestedOrganizationId, isMultiOrgEnabled: false));
            }

            var candidates = OrganizationScopeResolver.CollectStatusCandidates(user, requestedOrganizationId);
            IReadOnlyDictionary<string, bool> statuses = new Dictionary<string, bool>();
            if (candidates.Count > 0)
            {
                try
                {
                    statuses = await resourceRepository.GetOrganizationStatusesAsync(candidates);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Organization status could not be read while issuing a token for user {UserId}; nothing is issued.", user.ItemId);
                    return OrganizationScopeResolver.Refused(OrganizationIssuanceOutcome.StatusUnavailable);
                }
            }

            var decision = OrganizationScopeResolver.ResolveIssuance(
                user,
                requestedOrganizationId,
                isExplicit,
                isLinkOrganization,
                statuses,
                settings.AllowNoOrgSignInForDisabledOrganizations,
                HasOrganizationAccess);

            if (decision.Outcome == OrganizationIssuanceOutcome.OrganizationDisabled)
            {
                // Ids only: never organization names (spec #668 C10).
                logger.LogInformation("Token refused for user {UserId}: organization {OrganizationId} is disabled or no membership is active.", user.ItemId, requestedOrganizationId ?? user.LastUsedOrganizationId);
            }

            return decision;
        }

        /// <summary>
        /// The re-check at OIDC code and device-code exchange of the organization stored at authorize or approval
        /// (spec #668 C6). A stored organization is minted as before unless it is now disabled or missing, which
        /// refuses the exchange. A stored "no-org" (or blank) is decided again from the user's current memberships
        /// and the setting, so a status that changed in between applies (Q2). One status read at most.
        /// </summary>
        public static async Task<OrganizationIssuanceDecision> RecheckStoredOrganizationAsync(
            IResourceRepository resourceRepository,
            ILogger logger,
            User user,
            string? storedOrganizationId,
            OrganizationIssuanceSettings settings)
        {
            var stored = OrganizationScopeResolver.ForStoredOrganization(storedOrganizationId);
            if (!settings.IsMultiOrgEnabled
                || string.Equals(storedOrganizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return OrganizationScopeResolver.Issued(stored);
            }

            if (string.IsNullOrWhiteSpace(storedOrganizationId)
                || string.Equals(storedOrganizationId, IdpConstants.NoOrganizationId, StringComparison.Ordinal))
            {
                return await ResolveForIssuanceAsync(resourceRepository, logger, user, requestedOrganizationId: null, isExplicit: false, isLinkOrganization: false, settings);
            }

            IReadOnlyDictionary<string, bool> statuses;
            try
            {
                statuses = await resourceRepository.GetOrganizationStatusesAsync([storedOrganizationId]);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Organization status could not be read while issuing a token for user {UserId}; nothing is issued.", user.ItemId);
                return OrganizationScopeResolver.Refused(OrganizationIssuanceOutcome.StatusUnavailable);
            }

            if (statuses.TryGetValue(storedOrganizationId, out var isDisabled) && !isDisabled)
            {
                return OrganizationScopeResolver.Issued(stored);
            }

            logger.LogInformation("Token refused for user {UserId}: organization {OrganizationId} is disabled or no membership is active.", user.ItemId, storedOrganizationId);
            return OrganizationScopeResolver.Refused(OrganizationIssuanceOutcome.OrganizationDisabled);
        }

        /// <summary>
        /// The organization id to mint after <see cref="RecheckStoredOrganizationAsync"/>: the stored value itself
        /// (blank included, exactly as before) while the decision kept it, else the re-decided one.
        /// </summary>
        public static string? MintedOrganizationId(string? storedOrganizationId, OrganizationIssuanceDecision decision) =>
            decision.Scope == OrganizationScopeResolver.ForStoredOrganization(storedOrganizationId)
                ? storedOrganizationId
                : decision.Scope.ClaimValue;

        /// <summary>
        /// Membership is a three-way test -- OrganizationIds, Roles keys, or Permissions keys --
        /// because an organization can be granted through a role or permission assignment without
        /// ever being written to <see cref="User.OrganizationIds"/>.
        /// </summary>
        public static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }
    }
}
