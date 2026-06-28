using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.Entities;
using System.Linq.Expressions;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationRepository
    {
        #region Authentication
        Task<IdentityConfiguration> GetAuthenticationConfigurationAsync();
        Task UpdateAuthenticationConfigAsync(IdentityConfiguration configuration);
        #endregion

        Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "");
    }
}