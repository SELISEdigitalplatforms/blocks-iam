using Blocks.Genesis;
using Identifier.DomainService.Shared;
using Identifier.DomainService.Shared.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class DisableDomainBindingConsumer : IConsumer<DisableDomainBindingRequest>
    {
        private readonly IDomainManagementService _domainManagementService;

        public DisableDomainBindingConsumer(IDomainManagementService domainManagementService)
        {
            _domainManagementService = domainManagementService;
        }

        public async Task Consume(DisableDomainBindingRequest request)
        {
            await _domainManagementService.DisableDomainBindingAsync(request);
        }
    }
}
