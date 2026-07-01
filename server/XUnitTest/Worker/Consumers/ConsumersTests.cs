using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Worker.Consumers;

namespace XUnitTest.Worker.Consumers
{
    public class ResourceMutationConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new ResourceMutationEvent();
            var service = new Mock<IResourceMutationService>();
            service.Setup(s => s.ExecuteResourceMutationCommandAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            var consumer = new ResourceMutationConsumer(NullLogger<ResourceMutationConsumer>.Instance, service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class ResourceSetToPermissionMutationConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new ResourceSetToPermissionMutationEvent();
            var service = new Mock<IResourceMutationService>();
            service.Setup(s => s.ProcessPermissionAsync(@event)).Returns(Task.FromResult(true)).Verifiable();

            var consumer = new ResourceSetToPermissionMutationConsumer(NullLogger<ResourceSetToPermissionMutationConsumer>.Instance, service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class OrganizationProvisioningConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new OrganizationProvisioningEvent();
            var service = new Mock<IResourceMutationService>();
            service.Setup(s => s.ExecuteOrganizationProvisioningAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            var consumer = new OrganizationProvisioningConsumer(NullLogger<OrganizationProvisioningConsumer>.Instance, service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class PropagationRolePermissionUpdateConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new PropagationRolePermissionUpdateEvent();
            var service = new Mock<IResourceMutationService>();
            service.Setup(s => s.ExecutePropagationRolePermissionUpdateAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            var consumer = new PropagationRolePermissionUpdateConsumer(NullLogger<PropagationRolePermissionUpdateConsumer>.Instance, service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class UserMutationConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new UserMutationEvent();
            var service = new Mock<IUserManagementMutationService>();
            service.Setup(s => s.ExecuteUserMutationCommandAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            var consumer = new UserMutationConsumer(service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class CreateUserByEmailConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new CreateUserByEmailEvent();
            var service = new Mock<IUserManagementMutationService>();
            service.Setup(s => s.CreateUserByEmailAsync(@event)).Returns(Task.FromResult(true)).Verifiable();

            var consumer = new CreateUserByEmailConsumer(service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }

    public class CreateUserViaSsoConsumerTests
    {
        [Fact]
        public async Task Consume_DelegatesToService()
        {
            var @event = new CreateUserViaSsoEvent();
            var service = new Mock<IUserManagementMutationService>();
            service.Setup(s => s.ExecuteUserMutationViaSsoCommandAsync(@event)).Returns(Task.CompletedTask).Verifiable();

            var consumer = new CreateUserViaSsoConsumer(service.Object);

            await consumer.Consume(@event);

            service.Verify();
        }
    }
}
