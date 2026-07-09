namespace Iam.DomainService.Resources.TenantPropagation
{
    /// <summary>
    /// Resolves the propagation fan-out set on the worker side. The list of
    /// enabled non-source tenants is cached briefly (configurable; default
    /// 5 minutes) to keep a 5000-tenant deployment from hitting the root DB
    /// for every mutation event.
    /// </summary>
    public interface ITenantEnumeration
    {
        Task<IReadOnlyList<PermissionMutationTarget>> GetTargetsAsync(string? excludeTenantId);
    }
}
