namespace Iam.DomainService.Dtos
{
    /// <summary>
    /// The mail phase of a batch job is ready: its users are written and its mail plan is on the job. Only the
    /// ids travel, as for <see cref="BatchJobQueuedEvent"/>.
    /// </summary>
    public class BatchMailQueuedEvent
    {
        public string JobId { get; set; } = string.Empty;

        /// <summary>The tenant whose database holds the job; checked against the job's own tenant.</summary>
        public string TenantId { get; set; } = string.Empty;
    }
}
