using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Batch;
using Iam.DomainService.Notifications;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>The permission, queue, notification and service registrations the batch upsert adds.</summary>
    public class BatchRegistrationTests
    {
        [Fact]
        public void ImportUsers_IsAUserPermission_AndCountsAsManagePower_H16()
        {
            UserPermissions.ImportUsers.Should().Be("blocks-iam::iam::import-users");
            UserPermissions.All.Should().Contain(UserPermissions.ImportUsers);
            AccessGrantRules.IsManagePermission("blocks-iam::iam::import-users").Should().BeTrue();
            AccessGrantRules.IsManagePermission("BLOCKS-IAM::IAM::IMPORT-USERS").Should().BeTrue();
        }

        [Fact]
        public void BatchQueue_IsRegisteredForBothBrokers()
        {
            IdpConstants.IamBatchQueue.Should().Be("blocks_iam_listener_batch");

            var rabbit = IdpConstants.GetMessageConfiguration("amqp://<username>:<password>@localhost:5672/");
            rabbit.RabbitMqConfiguration!.ConsumerSubscriptions.Select(s => s.QueueName).Should().Contain(IdpConstants.IamBatchQueue);

            var azure = IdpConstants.GetMessageConfiguration("Endpoint=sb://test.servicebus.windows.net/");
            azure.AzureServiceBusConfiguration!.Queues.Should().Contain(IdpConstants.IamBatchQueue);
        }

        [Fact]
        public void FinishedNotification_HasItsName()
        {
            BuiltInNotificationNames.BatchJobFinished.Should().Be("iam-batch-job-finished");
            IamSchemas.BatchJob.Should().Be("BatchJob");
        }

        [Fact]
        public void BatchServices_AreRegisteredAsSingletons()
        {
            var services = new ServiceCollection();
            services.RegisterSharedServices();

            services.Should().Contain(d => d.ServiceType == typeof(IBatchJobRepository) && d.ImplementationType == typeof(BatchJobRepository) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(IBatchUserService) && d.ImplementationType == typeof(BatchUserService) && d.Lifetime == ServiceLifetime.Singleton);
        }

        [Theory]
        [InlineData(BatchJobStatus.Completed, true)]
        [InlineData(BatchJobStatus.CompletedWithErrors, true)]
        [InlineData(BatchJobStatus.Failed, true)]
        [InlineData(BatchJobStatus.Queued, false)]
        [InlineData(BatchJobStatus.Running, false)]
        [InlineData(null, false)]
        public void IsFinal_KnowsTheFinalStatuses(string? status, bool final) => BatchJobStatus.IsFinal(status).Should().Be(final);

        [Fact]
        public void ErrorMessages_ExistForEveryItemCode()
        {
            foreach (var code in typeof(BatchErrorCodes).GetFields()
                         .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                         .Select(f => (string)f.GetRawConstantValue()!)
                         .Except([BatchErrorCodes.ItemsRequired, BatchErrorCodes.ItemsLimitExceeded, BatchErrorCodes.DuplicateRef,
                             BatchErrorCodes.DuplicateEmail, BatchErrorCodes.ClientReferenceTooLong, BatchErrorCodes.QueueUnavailable,
                             BatchErrorCodes.TenantMismatch, BatchErrorCodes.ProcessingFailed]))
            {
                BatchErrorCodes.MessageFor(code).Should().NotBe(code, $"{code} needs an English message");
            }

            BatchErrorCodes.MessageFor("unknown").Should().Be("unknown");
        }
    }
}
