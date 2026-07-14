using Iam.DomainService.Dtos;

namespace Iam.DomainService.Resources.TenantPropagation
{
    public interface ITenantPermissionPropagator
    {
        Task<PropagationSummary> PropagateAsync(PermissionMutationForTenantsEvent context);
    }
}
