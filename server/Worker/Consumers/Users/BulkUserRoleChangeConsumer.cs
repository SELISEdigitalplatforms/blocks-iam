using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    /// <summary>
    /// Applies one chunk of a bulk role change.
    /// <para>
    /// Thin by design, like <see cref="UpdateOrganizationUserConsumer"/>: the loop, the delta and the
    /// per-user outcome logging all live in the domain service, so the preview endpoint and this
    /// consumer compute the change with the same code rather than two implementations that can drift.
    /// </para>
    /// <para>
    /// <see cref="IUserManagementMutationService.ApplyBulkRoleChangeAsync"/> swallows per-user
    /// failures, so <see cref="Consume"/> completes normally and the message is acknowledged exactly
    /// once. Letting one bad id throw would have the broker redeliver the whole chunk.
    /// </para>
    /// </summary>
    public class BulkUserRoleChangeConsumer : IConsumer<BulkUserRoleChangeEvent>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public BulkUserRoleChangeConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }

        public async Task Consume(BulkUserRoleChangeEvent context)
        {
            await _userManagementMutationService.ApplyBulkRoleChangeAsync(context);
        }
    }
}
