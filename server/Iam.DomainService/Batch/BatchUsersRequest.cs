using System.Text.Json;
using System.Text.Json.Serialization;
using Blocks.Genesis.Entities.Iam;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Batch
{
    /// <summary>POST iam/batch/users: up to 100 users to create or update, processed by the worker.</summary>
    public class BatchUsersRequest
    {
        public string? ClientReference { get; set; }
        public List<BatchUserItem>? Items { get; set; }
    }

    /// <summary>
    /// One user of a request. Every item is an upsert keyed by its email: created when no account has the
    /// email, otherwise added to (organizations added, roles and permissions merged, profile fields set).
    /// </summary>
    public class BatchUserItem
    {
        /// <summary>Optional caller id of the row (e.g. a legacy user id), echoed in failures and mail results.</summary>
        public string? Ref { get; set; }
        public string? Email { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Language { get; set; }
        public Dictionary<string, object>? Attributes { get; set; }
        public List<BatchMembership>? Memberships { get; set; }

        /// <summary>The mail to send after the import; none when absent.</summary>
        public BatchItemMail? Mail { get; set; }

        /// <summary>Fields outside the contract (password, active, operation, data, ...): ignored, and counted in the log.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
    }

    /// <summary>The mail of one item, sent by the mail phase after the users are written.</summary>
    [BsonIgnoreExtraElements]
    public class BatchItemMail
    {
        public bool Send { get; set; }

        /// <summary>Template (mail purpose) for a user this job creates.</summary>
        public string? Template { get; set; }

        /// <summary>Template for a user that already existed and that this job added to a new organization.</summary>
        public string? ExistingUserTemplate { get; set; }

        /// <summary>Template language; en-US when absent.</summary>
        public string? Language { get; set; }
    }

    /// <summary>One user as the job stores it. Holds no unbound JSON values.</summary>
    [BsonIgnoreExtraElements]
    public class BatchUserData
    {
        public string? Email { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Language { get; set; }

        /// <summary>Stored on the job as plain BSON values (no type wrappers), like the user's own attributes.</summary>
        [BsonSerializer(typeof(AttributeBagSerializer))]
        public Dictionary<string, object>? Attributes { get; set; }
        public List<BatchMembership>? Memberships { get; set; }

        [BsonIgnoreIfNull]
        public BatchItemMail? Mail { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class BatchMembership
    {
        public string? OrganizationId { get; set; }
        public List<string>? Roles { get; set; }
        public List<string>? Permissions { get; set; }
    }

    /// <summary>The 202 answer of POST iam/batch/users, or its 400 with <see cref="Errors"/>.</summary>
    public class BatchSubmitResponse
    {
        public bool IsSuccess { get; set; }
        public string? JobId { get; set; }
        public string? Status { get; set; }
        public int Total { get; set; }
        public int Rejected { get; set; }
        public IDictionary<string, string>? Errors { get; set; }
    }

    /// <summary>GET iam/batch/jobs/{jobId}.</summary>
    public class BatchJobResponse
    {
        public bool IsSuccess { get; set; }
        public string JobId { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string? ClientReference { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Total { get; set; }
        public BatchJobSummary Summary { get; set; } = new();
        public List<BatchItemFailure> Failures { get; set; } = [];
        public string? Error { get; set; }

        /// <summary>The mail phase; null when no item asked for a mail.</summary>
        public BatchJobMailResponse? Mail { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public static BatchJobResponse From(BatchJob job)
        {
            ArgumentNullException.ThrowIfNull(job);
            return new BatchJobResponse
            {
                IsSuccess = true,
                JobId = job.ItemId,
                Resource = job.Resource,
                ClientReference = job.ClientReference,
                Status = job.Status,
                Total = job.Total,
                Summary = job.Summary,
                Failures = job.Failures.OrderBy(f => f.Index).ToList(),
                Error = job.Error,
                Mail = BatchJobMailResponse.From(job.Mail),
                CreatedDate = job.CreatedDate,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            };
        }
    }

    /// <summary>The mail phase of a job as GET iam/batch/jobs/{jobId} returns it.</summary>
    public class BatchJobMailResponse
    {
        public string Status { get; set; } = string.Empty;
        public int Requested { get; set; }
        public int Queued { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        /// <summary>The mails that were not queued, by index. Queued mails are only counted.</summary>
        public List<BatchMailResult> Results { get; set; } = [];
        public string? Error { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public static BatchJobMailResponse? From(BatchJobMail? mail) => mail is null
            ? null
            : new BatchJobMailResponse
            {
                Status = mail.Status,
                Requested = mail.Requested,
                Queued = mail.Queued,
                Skipped = mail.Skipped,
                Failed = mail.Failed,
                Results = mail.Results.OrderBy(r => r.Index).ToList(),
                Error = mail.Error,
                StartedAt = mail.StartedAt,
                CompletedAt = mail.CompletedAt
            };
    }
}
