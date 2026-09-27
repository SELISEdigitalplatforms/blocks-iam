namespace Iam.DomainService.Dtos
{
    /// <summary>
    /// One chunk of a bulk role change, on its way to the worker.
    /// <para>
    /// The ids are <em>resolved at submit time</em> rather than the filter being re-run in the
    /// worker. Re-resolving would let the matched set drift between the number the operator approved
    /// in the preview and the number actually changed -- the one thing the preview exists to prevent.
    /// It also makes chunking and idempotent redelivery straightforward.
    /// </para>
    /// </summary>
    public class BulkUserRoleChangeEvent
    {
        /// <summary>Shared by every chunk of one submit; a log correlation id only.</summary>
        public required string BatchId { get; set; }

        public required string OrganizationId { get; set; }

        /// <summary>Captured from <c>BlocksContext</c> at submit, since the worker has no caller.</summary>
        public string? TenantId { get; set; }

        public List<string> AddRoles { get; set; } = new();

        public List<string> RemoveRoles { get; set; } = new();

        /// <summary>The resolved ids for this chunk; disjoint from every other chunk of the batch.</summary>
        public List<string> UserIds { get; set; } = new();
    }
}
