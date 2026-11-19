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
        public BatchRequestOptions? Options { get; set; }
        public List<BatchUserItem>? Items { get; set; }
    }

    public class BatchRequestOptions
    {
        /// <summary>Default for items that do not set <c>data.sendEmail</c>; false when absent.</summary>
        public bool? SendEmail { get; set; }
    }

    public class BatchUserItem
    {
        public string? Ref { get; set; }
        public string? Operation { get; set; }
        public BatchUserData? Data { get; set; }
    }

    /// <summary>One user of a batch. Also stored on the job, so it holds no unbound JSON values.</summary>
    public class BatchUserData
    {
        public string? Email { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Language { get; set; }
        public bool? SendEmail { get; set; }

        /// <summary>Stored on the job as plain BSON values (no type wrappers), like the user's own attributes.</summary>
        [BsonSerializer(typeof(AttributeBagSerializer))]
        public Dictionary<string, object>? Attributes { get; set; }
        public List<BatchMembership>? Memberships { get; set; }

        /// <summary>Fields outside the contract (password, active, ...): ignored, and their names logged.</summary>
        [JsonExtensionData]
        [BsonIgnore]
        public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
    }

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
                CreatedDate = job.CreatedDate,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            };
        }
    }
}
