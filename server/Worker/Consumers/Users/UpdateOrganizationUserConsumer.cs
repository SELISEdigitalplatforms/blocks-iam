using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class UpdateOrganizationUserConsumer : IConsumer<UpdateOrganizationUserEvent>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public UpdateOrganizationUserConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }
        public async Task Consume(UpdateOrganizationUserEvent context)
        {
            var request = new UpdateUserAccessControlRequest
            {
                OrganizationId = context.OrganizationId,
                UserId = context.UserId,
                Roles = context.Roles,
                Permissions = context.Permissions
            };
            await _userManagementMutationService.UpdateUserAccessControlAsync(request);
        }
    }
}
