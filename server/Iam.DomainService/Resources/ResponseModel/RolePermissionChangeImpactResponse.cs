namespace Iam.DomainService.Resources
{
    /// <summary>
    /// What applying a pending permission diff to a role would do, both inside the caller's own
    /// organization and -- if propagation is offered and accepted -- across every other copy of the
    /// role.
    /// </summary>
    /// <remarks>
    /// Read-only and current-at-read, exactly like <see cref="ArchiveImpactResponseBase"/>: nothing
    /// here locks anything, so a user granted the role between opening the dialog and confirming is
    /// still affected by the change. Copy built from these numbers must describe them as current,
    /// never as a guarantee.
    /// </remarks>
    public sealed class RolePermissionChangeImpactResponse
    {
        public bool IsSuccess { get; set; }

        public Dictionary<string, string>? Errors { get; set; }

        public string? Slug { get; set; }

        /// <summary>Display name of the role the diff applies to.</summary>
        public string? Name { get; set; }

        public bool IsMultiOrgEnabled { get; set; }

        /// <summary>
        /// Whether the propagation option may be offered at all. False collapses the dialog to a
        /// plain local confirmation.
        /// </summary>
        /// <remarks>
        /// Computed here rather than in the client so the rule lives in one place and cannot drift
        /// from the gate <c>ProcessPermissionAsync</c> actually enforces: multi-organization mode on,
        /// AND the role being edited is the default organization's copy. Without the second half an
        /// organization-scoped administrator would be shown a control that the backend silently
        /// ignores.
        /// </remarks>
        public bool CanPropagate { get; set; }

        /// <summary>Permissions being added that actually resolve to a document. </summary>
        public int AddCount { get; set; }

        public int RemoveCount { get; set; }

        /// <summary>
        /// Organizations holding a non-archived copy of this role, EXCLUDING the role's own, so the
        /// number reads as "N other organizations".
        /// </summary>
        public int OrganizationCount { get; set; }

        /// <summary>
        /// Organizations where propagation would be skipped because their copy of the role is
        /// missing or archived, or because they never received a copy of the permissions involved.
        /// Surfaced rather than hidden: a silent skip is exactly the drift this feature exists to
        /// stop accumulating.
        /// </summary>
        public int SkippedOrganizationCount { get; set; }

        /// <summary>
        /// DISTINCT users currently holding this role across the counted organizations. This is the
        /// population whose effective access changes -- upward for adds, downward for removes.
        /// Unfiltered by user state, matching how the write itself is unfiltered.
        /// </summary>
        public int AffectedUserCount { get; set; }

        /// <summary>
        /// Subset of <see cref="AffectedUserCount"/> that is genuinely active: the live access being
        /// granted or revoked, which is the number a confirmation dialog should emphasise.
        /// </summary>
        public int ActiveUserCount { get; set; }
    }
}
