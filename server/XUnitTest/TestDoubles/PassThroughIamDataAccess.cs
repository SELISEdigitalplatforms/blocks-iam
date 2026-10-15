using System.Threading.Tasks;
using Iam.DomainService.Services;
using MongoDB.Driver;

namespace XUnitTest.TestDoubles;

/// <summary>Allows everything and leaves filters alone, so repository tests keep testing the repository.</summary>
internal sealed class PassThroughIamDataAccess : IIamDataAccess
{
    public Task<ReadScope<T>> ReadAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null, bool useProjection = true) =>
        Task.FromResult(new ReadScope<T>(true, filter, null));

    public Task<FilterDefinition<T>?> EditFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null) =>
        Task.FromResult<FilterDefinition<T>?>(filter);

    public Task<FilterDefinition<T>?> DeleteFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null) =>
        Task.FromResult<FilterDefinition<T>?>(filter);

    public Task<bool> InsertAllowedAsync(string schema, string callSite, string? tenantId = null) => Task.FromResult(true);
}
