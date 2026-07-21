using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;

namespace Worker.Consumers
{
    public class PropagationRolePermissionUpdateConsumer : IConsumer<PropagationRolePermissionUpdateEvent>
    {
        private readonly ILogger<PropagationRolePermissionUpdateConsumer> _logger;
        private readonly IResourceMutationService _resourceMutationService;

        public PropagationRolePermissionUpdateConsumer(ILogger<PropagationRolePermissionUpdateConsumer> logger, IResourceMutationService resourceMutationService)
        {
            _logger = logger;
            _resourceMutationService = resourceMutationService;
        }

        public async Task Consume(PropagationRolePermissionUpdateEvent context)
        {
            _logger.LogInformation("Start Consume for PropagationRolePermissionUpdate");
            await _resourceMutationService.ExecutePropagationRolePermissionUpdateAsync(context);
        }
    }
}
