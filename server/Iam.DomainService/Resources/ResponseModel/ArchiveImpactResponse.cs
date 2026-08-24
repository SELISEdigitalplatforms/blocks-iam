namespace Iam.DomainService.Resources
{
    /// <summary>
    /// What archiving a role or permission would actually do, so a caller can show the consequence
    /// before asking for consent. Read-only: nothing here reserves or locks anything, and the
    /// numbers are current-at-read (a user assigned a moment later is still affected).
    /// </summary>
    public abstract class ArchiveImpactResponseBase
    {
        public bool IsSuccess { get; set; }

        public Dictionary<string, string>? Errors { get; set; }

        public string? Name { get; set; }

        public bool IsMultiOrgEnabled { get; set; }

        /// <summary>
        /// Organizations holding a non-archived copy, EXCLUDING the target's own organization, so
        /// the number reads as "N other organizations are affected".
        /// </summary>
        public int OrganizationCount { get; set; }

        /// <summary>
        /// DISTINCT users who would lose this, across the target organization and every counted
        /// copy. Unfiltered by user state, because the scrub itself is unfiltered.
        /// </summary>
        public int AffectedUserCount { get; set; }

        /// <summary>
        /// True when this role or permission is one of the tenant's signup defaults, so archiving it
        /// also changes what every new account receives. Reported separately from the user count
        /// because it is a different population: not people who hold it now, but everyone who would
        /// have been given it from here on. Tenant-wide, so it is unaffected by organization scope.
        /// </summary>
        public bool IsSignUpDefault { get; set; }

        /// <summary>True when no consent can make the archive proceed.</summary>
        public bool Blocked { get; set; }

        public string? BlockingReason { get; set; }
    }

    public sealed class RoleArchiveImpactResponse : ArchiveImpactResponseBase
    {
        public string? Slug { get; set; }

        /// <summary>
        /// Subset of <see cref="ArchiveImpactResponseBase.AffectedUserCount"/> that is genuinely
        /// active. Reported separately because it is the number representing live access being
        /// revoked, which is what a confirmation dialog needs to emphasise.
        /// </summary>
        public int ActiveUserCount { get; set; }
    }

    public sealed class PermissionArchiveImpactResponse : ArchiveImpactResponseBase
    {
        public string? Resource { get; set; }

        /// <summary>DISTINCT role slugs referencing this permission in the counted organizations.</summary>
        public int RoleBindingCount { get; set; }
    }
}
