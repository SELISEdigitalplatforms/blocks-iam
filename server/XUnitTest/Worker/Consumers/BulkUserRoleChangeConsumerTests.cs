using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using Moq;
using Worker.Consumers;

namespace XUnitTest.Worker.Consumers
{
    /// <summary>
    /// The consumer is deliberately thin -- the loop, the delta and the per-user outcome logging all
    /// live in the domain service so the preview endpoint and the worker compute the change with the
    /// same code. Those are covered in
    /// <c>UserManagementMutationServiceBulkRoleTests</c>; what is left to prove here is the
    /// delegation and, most importantly, that nothing escapes <see cref="BulkUserRoleChangeConsumer.Consume"/>
    /// to make the broker redeliver the chunk.
    /// </summary>
    public class BulkUserRoleChangeConsumerTests
    {
        private static BulkUserRoleChangeEvent Event() => new()
        {
            BatchId = "b_1",
            OrganizationId = "org_acme",
            TenantId = "tenant-1",
            AddRoles = ["viewer"],
            UserIds = ["u1", "u2"]
        };

        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = Event();
            var service = new Mock<IUserManagementMutationService>();
            service.Setup(s => s.ApplyBulkRoleChangeAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            await new BulkUserRoleChangeConsumer(service.Object).Consume(@event);

            service.Verify();
        }

        [Fact]
        public async Task Consume_AddsNoRetryOrSwallowingOfItsOwn()
        {
            // H6/A6 -- the swallowing is the service's job, per user, so a genuinely broken service
            // call still surfaces here rather than being silently acknowledged twice over. This pins
            // the consumer as a pass-through and stops a well-meaning try/catch being added to it.
            var service = new Mock<IUserManagementMutationService>();
            service.Setup(s => s.ApplyBulkRoleChangeAsync(It.IsAny<BulkUserRoleChangeEvent>()))
                .ThrowsAsync(new InvalidOperationException("service unavailable"));

            var act = async () => await new BulkUserRoleChangeConsumer(service.Object).Consume(Event());

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
