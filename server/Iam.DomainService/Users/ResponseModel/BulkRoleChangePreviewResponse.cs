using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// The dry-run answer: what a <see cref="BulkRoleChangeRequest"/> would do, having written
    /// nothing. This is the only safety gate the operator gets -- the submit endpoint hands the work
    /// to a background worker that reports no progress, so anything not caught here stays invisible
    /// until someone notices a wrong role in production.
    /// </summary>
    public class BulkRoleChangePreviewResponse : BaseResponse
    {
        /// <summary>Users the target resolves to, inside <c>OrganizationId</c>.</summary>
        public long MatchedCount { get; set; }

        /// <summary>Of those, the ones whose role list would actually change.</summary>
        public long AffectedCount { get; set; }

        /// <summary>Matched, but the delta is a no-op for them.</summary>
        public long UnchangedCount { get; set; }

        // INVARIANT: MatchedCount == AffectedCount + UnchangedCount. There is no third category:
        // no per-user role cap exists anywhere in this repo, so a matched user either changes or
        // does not.
    }
}
