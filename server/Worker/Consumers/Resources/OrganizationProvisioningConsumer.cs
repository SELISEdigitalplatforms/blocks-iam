using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;

namespace Worker.Consumers
{
    public class OrganizationProvisioningConsumer : IConsumer<OrganizationProvisioningEvent>
    {
        private readonly ILogger<OrganizationProvisioningConsumer> _logger;
        private readonly IResourceMutationService _resourceMutationService;

        public OrganizationProvisioningConsumer(ILogger<OrganizationProvisioningConsumer> logger, IResourceMutationService resourceMutationService)
        {
            _logger = logger;
            _resourceMutationService = resourceMutationService;
        }

        public async Task Consume(OrganizationProvisioningEvent context)
        {
            _logger.LogInformation("Start Consume for OrganizationProvisioningEvent");
            await _resourceMutationService.ExecuteOrganizationProvisioningAsync(context);
        }
    }
}
