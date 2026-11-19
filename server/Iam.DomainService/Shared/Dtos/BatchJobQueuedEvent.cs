namespace Iam.DomainService.Dtos
{
    /// <summary>
    /// A batch job is ready for the worker. Only the ids travel: the items stay in the job document,
    /// so the message has a constant size whatever the batch holds.
    /// </summary>
    public class BatchJobQueuedEvent
    {
        public string JobId { get; set; } = string.Empty;

        /// <summary>The tenant whose database holds the job; checked against the job's own tenant.</summary>
        public string TenantId { get; set; } = string.Empty;
    }
}
