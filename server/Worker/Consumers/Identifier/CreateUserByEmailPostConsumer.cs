using Blocks.Genesis;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.People;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class CreateUserByEmailPostConsumer : IConsumer<CreateUserByEmailPostEvent_Identifier>
    {
        private readonly IPeopleService _peopleService;

        public CreateUserByEmailPostConsumer(IPeopleService peopleService)
        {
            _peopleService = peopleService;
        }

        public async Task Consume(CreateUserByEmailPostEvent_Identifier context)
        {
            await _peopleService.SendProjectInvitationToNewUser(context);
        }
    }
}
