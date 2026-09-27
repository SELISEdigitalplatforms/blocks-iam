using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// The answer to a submit: accepted and queued, not applied. Carried by an HTTP 202, because the
    /// status code is the only place the contract can say "this has not happened yet".
    /// </summary>
    public class BulkRoleChangeSubmitResponse : BaseResponse
    {
        /// <summary>
        /// Correlation id stamped on every log line the worker writes for this batch.
        /// <strong>Not a pollable handle</strong> -- there is no job document and no status endpoint.
        /// </summary>
        public string BatchId { get; set; } = string.Empty;

        /// <summary>Users the target resolved to at submit time; the ids travel on the queue.</summary>
        public long MatchedCount { get; set; }
    }
}
