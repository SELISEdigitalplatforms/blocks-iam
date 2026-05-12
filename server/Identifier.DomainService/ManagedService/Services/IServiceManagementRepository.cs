using Identifier.DomainService.Shared.Entities;

namespace Identifier.DomainService.ManagedService.Services
{
    public interface IServiceManagementRepository
    {
        Task SaveAsync(BlocksManagedService service);
        Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync(GetAllServiceRequest request);
    }
}
