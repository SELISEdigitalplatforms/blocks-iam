using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Batch
{
    /// <summary>Job statuses, stored as strings so the documents read the same as the API.</summary>
    public static class BatchJobStatus
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string CompletedWithErrors = "CompletedWithErrors";
        public const string Failed = "Failed";

        public static bool IsFinal(string? status) =>
            status is Completed or CompletedWithErrors or Failed;
    }

    /// <summary>The resources a batch job can carry. Only users so far; the envelope is generic.</summary>
    public static class BatchResources
    {
        public const string Users = "users";
    }

    /// <summary>
    /// One batch request and its result, in the tenant database (<c>BatchJobs</c>). The accepted items
    /// are kept only until the job reaches a final status; after that only the summary and the failed
    /// users (by email, no other personal data) remain.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class BatchJob : BaseEntity
    {
        public string Resource { get; set; } = BatchResources.Users;
        public string Status { get; set; } = BatchJobStatus.Queued;
        public string TenantId { get; set; } = string.Empty;
        public string? ClientReference { get; set; }

        /// <summary>The submitting user; null for a client or platform caller.</summary>
        public string? RequestedBy { get; set; }

        /// <summary>The submitter's organization when it submitted.</summary>
        public string? RequestedByOrganizationId { get; set; }

        /// <summary>The submitter was the system user (the default organization, or a platform call).</summary>
        public bool RequestedBySystemCaller { get; set; }

        /// <summary>The submitting client credential when there was no user.</summary>
        public string? RequestedByClientId { get; set; }

        /// <summary>The accepted items; removed when the job reaches a final status.</summary>
        [BsonIgnoreIfNull]
        public List<BatchJobItem>? Items { get; set; }

        /// <summary>Side effects planned before the user write and not yet sent; replayed when a job resumes.</summary>
        public List<BatchPendingSideEffect> PendingSideEffects { get; set; } = [];

        /// <summary>Every item of the request, rejected ones included.</summary>
        public int Total { get; set; }

        public BatchJobSummary Summary { get; set; } = new();
        public List<BatchItemFailure> Failures { get; set; } = [];

        /// <summary>A job-level failure code; null unless the job failed as a whole.</summary>
        public string? Error { get; set; }

        /// <summary>The mail phase, set when the import finishes; null when no item asked for a mail.</summary>
        [BsonIgnoreIfNull]
        public BatchJobMail? Mail { get; set; }

        /// <summary>The mails the mail phase still has to send; removed when the mail phase finishes.</summary>
        [BsonIgnoreIfNull]
        public List<BatchMailEntry>? MailPlan { get; set; }

        public int Attempt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? HeartbeatAt { get; set; }
    }

    /// <summary>An accepted item, kept with its index in the request.</summary>
    [BsonIgnoreExtraElements]
    public class BatchJobItem
    {
        public int Index { get; set; }

        [BsonIgnoreIfNull]
        public string? Ref { get; set; }
        public BatchUserData Data { get; set; } = new();
    }

    [BsonIgnoreExtraElements]
    public class BatchJobSummary
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Failed { get; set; }
    }

    /// <summary>A user that was rejected or failed, by index, email and (when given) ref only.</summary>
    [BsonIgnoreExtraElements]
    public class BatchItemFailure
    {
        public string? Ref { get; set; }
        public int Index { get; set; }
        public string? Email { get; set; }
        public List<BatchItemError> Errors { get; set; } = [];
    }

    [BsonIgnoreExtraElements]
    public class BatchItemError
    {
        public string Field { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public static class BatchSideEffectKinds
    {
        /// <summary>The timeline entry and the user created action of a new account.</summary>
        public const string Created = "Created";
    }

    [BsonIgnoreExtraElements]
    public class BatchPendingSideEffect
    {
        public string UserId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
    }

    /// <summary>The outcome of one mail that was not queued.</summary>
    public static class BatchMailOutcomes
    {
        public const string Skipped = "Skipped";
        public const string Failed = "Failed";
    }

    /// <summary>The mail phase of a job: counts, and the mails that were not queued.</summary>
    [BsonIgnoreExtraElements]
    public class BatchJobMail
    {
        /// <summary><see cref="BatchJobStatus"/>: Queued, Running, Completed, CompletedWithErrors or Failed.</summary>
        public string Status { get; set; } = BatchJobStatus.Queued;

        /// <summary>Items that asked for a mail (mail.send true), the import's failures included.</summary>
        public int Requested { get; set; }
        public int Queued { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<BatchMailResult> Results { get; set; } = [];

        /// <summary>A mail-phase failure code; null unless the mail phase failed as a whole.</summary>
        public string? Error { get; set; }
        public int Attempt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>A mail that was skipped or failed.</summary>
    [BsonIgnoreExtraElements]
    public class BatchMailResult
    {
        public string? Ref { get; set; }
        public int Index { get; set; }
        public string? Email { get; set; }
        public string Outcome { get; set; } = BatchMailOutcomes.Skipped;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        [BsonIgnoreIfNull]
        public string? Template { get; set; }

        [BsonIgnoreIfNull]
        public string? Language { get; set; }
    }

    /// <summary>A mail the mail phase sends: one user, one template, the organization whose template is used.</summary>
    [BsonIgnoreExtraElements]
    public class BatchMailEntry
    {
        public int Index { get; set; }

        [BsonIgnoreIfNull]
        public string? Ref { get; set; }
        public string Email { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string Language { get; set; } = BatchLimits.DefaultMailLanguage;

        [BsonIgnoreIfNull]
        public string? FirstName { get; set; }

        [BsonIgnoreIfNull]
        public string? LastName { get; set; }

        [BsonIgnoreIfNull]
        public string? OrganizationName { get; set; }
    }
}
