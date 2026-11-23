using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentValidation;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Notifications;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// <see cref="IBatchJobRepository"/> in memory, with the one rule the database enforces: at most one
    /// running job per resource (the unique partial index).
    /// </summary>
    internal sealed class InMemoryBatchJobRepository : IBatchJobRepository
    {
        public Dictionary<string, BatchJob> Jobs { get; } = new(StringComparer.Ordinal);

        public List<List<BatchPendingSideEffect>> PendingWrites { get; } = [];

        public int StartAttempts { get; private set; }

        public Task InsertAsync(BatchJob job)
        {
            Jobs[job.ItemId] = job;
            return Task.CompletedTask;
        }

        public Task<BatchJob?> GetAsync(string jobId, string tenantId) => Task.FromResult(Jobs.GetValueOrDefault(jobId));

        public Task<BatchJobStart> TryStartAsync(string jobId, string tenantId, DateTime now, DateTime staleBefore)
        {
            StartAttempts++;
            var job = Jobs[jobId];
            var startable = job.Status == BatchJobStatus.Queued
                || (job.Status == BatchJobStatus.Running && job.HeartbeatAt < staleBefore);
            if (!startable)
            {
                return Task.FromResult(new BatchJobStart(null, false));
            }

            if (Jobs.Values.Any(j => j.ItemId != jobId && j.Resource == job.Resource && j.Status == BatchJobStatus.Running))
            {
                return Task.FromResult(new BatchJobStart(null, true));
            }

            job.Status = BatchJobStatus.Running;
            job.HeartbeatAt = now;
            job.Attempt++;
            job.StartedAt ??= now;
            return Task.FromResult(new BatchJobStart(job, false));
        }

        public Task<BatchJob?> GetRunningAsync(string resource, string tenantId) =>
            Task.FromResult(Jobs.Values.FirstOrDefault(j => j.Resource == resource && j.Status == BatchJobStatus.Running));

        public Task<bool> RequeueStaleAsync(string jobId, string tenantId, DateTime? heartbeatAt)
        {
            var job = Jobs[jobId];
            if (job.Status != BatchJobStatus.Running || job.HeartbeatAt != heartbeatAt)
            {
                return Task.FromResult(false);
            }

            job.Status = BatchJobStatus.Queued;
            return Task.FromResult(true);
        }

        public int Heartbeats { get; private set; }

        /// <summary>Thrown by the next <see cref="SetPendingSideEffectsAsync"/> call that clears the list, once.</summary>
        public Exception? FailNextClear { get; set; }

        /// <summary>When set, every heartbeat throws it (a database blip after the write).</summary>
        public Exception? FailHeartbeats { get; set; }

        public Task HeartbeatAsync(string jobId, string tenantId, DateTime now, int? attempt = null)
        {
            if (FailHeartbeats is { } failure)
            {
                throw failure;
            }

            if (attempt is null || Jobs[jobId].Attempt == attempt)
            {
                Heartbeats++;
                Jobs[jobId].HeartbeatAt = now;
            }

            return Task.CompletedTask;
        }

        public Task SetPendingSideEffectsAsync(string jobId, string tenantId, List<BatchPendingSideEffect> sideEffects, DateTime now, int? attempt = null)
        {
            if (sideEffects.Count == 0 && FailNextClear is { } failure)
            {
                FailNextClear = null;
                throw failure;
            }

            if (attempt is null || Jobs[jobId].Attempt == attempt)
            {
                Jobs[jobId].PendingSideEffects = sideEffects.ToList();
                PendingWrites.Add(sideEffects.ToList());
            }

            return Task.CompletedTask;
        }

        public Task<bool> FinishAsync(string jobId, string tenantId, BatchJobFinish finish, IReadOnlyCollection<string> fromStatuses, int? attempt = null)
        {
            var job = Jobs[jobId];
            if (!fromStatuses.Contains(job.Status) || (attempt is not null && job.Attempt != attempt))
            {
                return Task.FromResult(false);
            }

            job.Status = finish.Status;
            job.Summary = finish.Summary;
            job.Failures = finish.Failures.OrderBy(f => f.Index).ToList();
            job.Error = finish.Error;
            job.CompletedAt = finish.CompletedAt;
            job.Items = null;
            job.PendingSideEffects = [];
            if (finish.Mail is not null)
            {
                job.Mail = finish.Mail;
                job.MailPlan = finish.MailPlan is { Count: > 0 } ? finish.MailPlan.ToList() : null;
            }

            return Task.FromResult(true);
        }

        public Task<BatchJob?> TryStartMailAsync(string jobId, string tenantId, DateTime now, DateTime staleBefore)
        {
            var job = Jobs.GetValueOrDefault(jobId);
            var mail = job?.Mail;
            var startable = mail is not null
                && (mail.Status == BatchJobStatus.Queued || (mail.Status == BatchJobStatus.Running && mail.StartedAt < staleBefore));
            if (!startable)
            {
                return Task.FromResult<BatchJob?>(null);
            }

            mail!.Status = BatchJobStatus.Running;
            mail.StartedAt = now;
            mail.Attempt++;
            return Task.FromResult(job);
        }

        public Task<bool> FinishMailAsync(string jobId, string tenantId, BatchJobMail mail, IReadOnlyCollection<string> fromStatuses, int? attempt = null)
        {
            var job = Jobs[jobId];
            if (job.Mail is null || !fromStatuses.Contains(job.Mail.Status) || (attempt is not null && job.Mail.Attempt != attempt))
            {
                return Task.FromResult(false);
            }

            mail.Results = mail.Results.OrderBy(r => r.Index).ToList();
            job.Mail = mail;
            job.MailPlan = null;
            return Task.FromResult(true);
        }

        public Task<BatchJob?> GetOldestQueuedAsync(string resource, string tenantId) =>
            Task.FromResult(Jobs.Values
                .Where(j => j.Resource == resource && j.Status == BatchJobStatus.Queued && j.TenantId == tenantId)
                .OrderBy(j => j.CreatedDate)
                .FirstOrDefault());
    }

    /// <summary>Collects formatted log lines of any category.</summary>
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    /// <summary>
    /// A <see cref="BatchUserService"/> over an in-memory job store, mocked user and resource repositories, the
    /// real access grant service (<see cref="AccessGrantTestKit"/>) and the real <c>MapUser</c>.
    /// </summary>
    internal sealed class BatchHarness
    {
        public const string Tenant = AccessGrantTestKit.Tenant;
        public const string Caller = "caller";
        public const string OrgA = "org-a";
        public const string OrgB = "org-b";

        public AccessGrantTestKit Kit { get; } = new();
        public InMemoryBatchJobRepository Jobs { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IResourceRepository> Resources { get; } = new();
        public Mock<IUserManagementMutationService> Mutations { get; } = new();
        public Mock<IUserActivityRepository> Activities { get; } = new();
        public List<UserActivity> WrittenActivities { get; } = [];
        public Mock<IMessageClient> Messages { get; } = new();
        public Mock<IBuiltInNotificationSender> Notifications { get; } = new();
        public CapturingLogger<BatchUserService> Logger { get; } = new();

        public List<User> StoredUsers { get; } = [];
        public Dictionary<string, Organization> Organizations { get; } = new(StringComparer.Ordinal);
        public List<Role> Roles { get; } = [];
        public bool MultiOrg { get; set; } = true;

        public List<(List<User> Inserts, List<UserBulkUpdate> Updates)> BulkWrites { get; } = [];
        public UserBulkWriteResult WriteResult { get; set; } = new(new HashSet<string>(), new HashSet<string>());
        public List<BuiltInNotification> SentNotifications { get; } = [];
        public List<ConsumerMessage<BatchJobQueuedEvent>> Published { get; } = [];
        public List<ConsumerMessage<BatchMailQueuedEvent>> PublishedMail { get; } = [];

        public BatchHarness()
        {
            Resources.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(() => new IamTenantConfiguration { IsMultiOrgEnabled = MultiOrg });
            Resources.Setup(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync((List<string> ids) => ids.Where(Organizations.ContainsKey).Select(id => Organizations[id]).ToList());
            Resources.Setup(r => r.GetRolesBySlugAndOrgAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync((List<string> slugs, string org) => Roles.Where(r => r.OrganizationId == org && slugs.Contains(r.Slug)).ToList());

            Users.Setup(u => u.GetUsersByEmailsAsync(It.IsAny<IReadOnlyCollection<string>>()))
                .ReturnsAsync((IReadOnlyCollection<string> emails) => StoredUsers.Where(u => emails.Contains(u.Email)).ToList());
            Users.Setup(u => u.GetUsersByIdsAsync(It.IsAny<IReadOnlyCollection<string>>()))
                .ReturnsAsync((IReadOnlyCollection<string> ids) => StoredUsers.Where(u => ids.Contains(u.ItemId)).ToList());
            Users.Setup(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<User>>(), It.IsAny<IReadOnlyList<UserBulkUpdate>>()))
                .ReturnsAsync((IReadOnlyList<User> inserts, IReadOnlyList<UserBulkUpdate> updates) =>
                {
                    BulkWrites.Add((inserts.ToList(), updates.ToList()));
                    return WriteResult;
                });

            var real = RealMutationService();
            Mutations.Setup(m => m.MapUser(It.IsAny<CreateUserRequest>())).Returns((CreateUserRequest r) => real.MapUser(r));

            Notifications.Setup(n => n.SendAsync(It.IsAny<BuiltInNotification>()))
                .Callback<BuiltInNotification>(SentNotifications.Add)
                .ReturnsAsync(true);
            Messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BatchJobQueuedEvent>>()))
                .Callback<ConsumerMessage<BatchJobQueuedEvent>>(Published.Add)
                .Returns(Task.CompletedTask);
            Messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BatchMailQueuedEvent>>()))
                .Callback<ConsumerMessage<BatchMailQueuedEvent>>(PublishedMail.Add)
                .Returns(Task.CompletedTask);
            Activities.Setup(a => a.InsertManyAsync(It.IsAny<IReadOnlyCollection<UserActivity>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyCollection<UserActivity>, CancellationToken>((activities, _) => WrittenActivities.AddRange(activities))
                .Returns(Task.CompletedTask);

            WithOrganization(OrgA).WithOrganization(OrgB);
            WithRoles(OrgA, "viewer", "editor", "admin", "manager").WithRoles(OrgB, "viewer", "editor").WithRoles("default", "base", "e2e-batch-role");

            // The usual submitter: a user of the default organization (the system user for the grant rules)
            // whose own role carries both per-user permissions (Q1: B).
            Kit.WithRole("tenant-admin", organizationId: "default")
                .WithPermission(UserPermissions.ManageUserRoles, "default", "tenant-admin")
                .WithPermission(UserPermissions.UpdateUserProfiles, "default", "tenant-admin")
                .WithPermission(UserPermissions.ImportUsers, "default", "tenant-admin", "importer");
            Kit.WithRole("importer", organizationId: "default");
            Kit.WithUser(Caller, ["tenant-admin"], organizationId: "default");
        }

        private static UserManagementMutationService RealMutationService() =>
            new(NullLogger<UserManagementMutationService>.Instance,
                new Mock<IValidator<CreateUserRequest>>().Object,
                new Mock<IValidator<UpdateUserRequest>>().Object,
                new Mock<IValidator<UpdateMyAccountRequest>>().Object,
                new Mock<IIdentityAccessManagementService>().Object,
                new Mock<IUserRepository>().Object,
                new Mock<IMessageClient>().Object,
                new Mock<ICacheClient>().Object,
                new Mock<ITenants>().Object,
                new Mock<IUserActivityDispatcher>().Object);

        /// <summary>The worker's clock; each simulated wait moves it forward.</summary>
        public DateTime Now { get; set; } = DateTime.UtcNow;

        /// <summary>Runs on every simulated wait, after the clock moved: lets a test change the world meanwhile.</summary>
        public Action? OnDelay { get; set; }

        public int Delays { get; private set; }

        public BatchUserService Service() =>
            new(Jobs, new BatchUserStores(Users.Object, Resources.Object, Activities.Object), Kit.Service(), Mutations.Object, Messages.Object, Logger, Notifications.Object)
            {
                UtcNow = () => Now,
                Delay = interval =>
                {
                    Delays++;
                    Now += interval;
                    OnDelay?.Invoke();
                    return Task.CompletedTask;
                }
            };

        public BatchHarness WithOrganization(string id, bool disabled = false, params string[] defaultRoles)
        {
            Organizations[id] = new Organization { ItemId = id, Name = id, IsDisabled = disabled, DefaultRoleForMembers = defaultRoles.ToList() };
            return this;
        }

        public BatchHarness WithRoles(string organizationId, params string[] slugs)
        {
            Roles.AddRange(slugs.Select(s => new Role { ItemId = $"{organizationId}-{s}", Slug = s, Name = s, OrganizationId = organizationId }));
            return this;
        }

        public User WithStoredUser(string id, string email, Dictionary<string, List<string>>? roles = null, Dictionary<string, List<string>>? permissions = null)
        {
            var user = new User
            {
                ItemId = id,
                Email = email,
                FirstName = "First",
                LastName = "Last",
                Active = true,
                Status = UserLifecycleStatus.Active,
                Roles = roles ?? new Dictionary<string, List<string>>(),
                Permissions = permissions ?? new Dictionary<string, List<string>>(),
                OrganizationIds = (roles ?? new Dictionary<string, List<string>>()).Keys.ToList()
            };
            StoredUsers.Add(user);
            return user;
        }

        public static BatchMembership Membership(string? organizationId, string[]? roles = null, string[]? permissions = null) => new()
        {
            OrganizationId = organizationId,
            Roles = roles?.ToList(),
            Permissions = permissions?.ToList()
        };

        public static BatchJobItem Item(int index, string email, params BatchMembership[] memberships) => new()
        {
            Index = index,
            Ref = $"ref-{index}",
            Data = new BatchUserData { Email = email, Memberships = memberships.ToList() }
        };

        /// <summary>A queued job submitted by <see cref="Caller"/> from the default organization.</summary>
        public BatchJob QueueJob(string id, params BatchJobItem[] items) =>
            QueueJob(id, DateTime.UtcNow, items);

        public BatchJob QueueJob(string id, DateTime createdDate, params BatchJobItem[] items)
        {
            var job = new BatchJob
            {
                ItemId = id,
                TenantId = Tenant,
                Status = BatchJobStatus.Queued,
                Items = items.ToList(),
                Total = items.Length,
                RequestedBy = Caller,
                RequestedByOrganizationId = "default",
                RequestedBySystemCaller = true,
                CreatedDate = createdDate
            };
            Jobs.Jobs[id] = job;
            return job;
        }

        public Task ProcessAsync(BatchJob job) =>
            Service().ProcessAsync(new BatchJobQueuedEvent { JobId = job.ItemId, TenantId = job.TenantId });

        public static BsonDocument Render(UpdateDefinition<User> update) =>
            update.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;
    }
}
