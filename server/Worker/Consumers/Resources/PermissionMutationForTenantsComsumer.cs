using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;

namespace Worker.Consumers
{
    public class PermissionMutationForTenantsConsumer : IConsumer<PermissionMutationForTenantsEvent>
    {
        private readonly ILogger<PermissionMutationForTenantsConsumer> _logger;
        private readonly IResourceMutationService _resourceMutationService;

        public PermissionMutationForTenantsConsumer(ILogger<PermissionMutationForTenantsConsumer> logger, IResourceMutationService resourceMutationService)
        {
            _logger = logger;
            _resourceMutationService = resourceMutationService;
        }

        public async Task Consume(PermissionMutationForTenantsEvent context)
        {
            _logger.LogInformation("Start Consume for ExecutePermissionMutationForTenantsAsync");
            await _resourceMutationService.ExecutePermissionMutationForTenantsAsync(context);
        }
    }
}
