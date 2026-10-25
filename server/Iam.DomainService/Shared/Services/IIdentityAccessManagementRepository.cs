using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users.RequestModel;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    public interface IIdentityAccessManagementRepository
    {
        IMongoCollection<T> GetCollection<T>();
        IMongoCollection<T> GetCollection<T>(string tenantId);
        IMongoCollection<T> GetCollectionByName<T>(string collectionName);
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<IamConfiguration> GetIamConfigurationAsync();
        Task<bool> CheckPasswordBlackListedAsync(string password);

        /// <summary>Idempotently ensures the (Key, Value) index on the root blacklist collection.</summary>
        Task EnsureIndexesAsync(CancellationToken ct = default);
        Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap);
        Task<bool> UpdateUserKeyMapActivationAsync(string userId);
        Task<List<UserKeyMap>> GetActiveUserKeyMapAsync(string userId);

        /// <summary>
        /// Marks one unused, unexpired code of the user as used, in one conditional write. True only for
        /// the single call that made the change, so a used, expired or unknown code - or a second request
        /// racing the first - gets false.
        /// </summary>
        Task<bool> TryConsumeUserKeyMapAsync(string key, string userId, DateTime nowUtc);
        Task<bool> UpdateUserAsync(User user);

        /// <summary>
        /// Records one successful login in a single atomic update: LogInCount is incremented
        /// server-side with $inc, and the last-login fields are set alongside it.
        ///
        /// Deliberately not <see cref="UpdateUserAsync"/>: that is a whole-document replace, and
        /// the read-increment-replace it used to serve here lost an increment whenever two writes
        /// to the same user overlapped - two logins at once, or a login landing while another
        /// request was writing the same document.
        ///
        /// Returns false when no user matched the id, so a genuine miss can be logged rather than
        /// passing silently as it did when only the replace's acknowledgement was available.
        /// </summary>
        Task<bool> RecordSuccessfulLoginAsync(string userId, string deviceInformationJson, DateTime nowUtc);
        Task<string> GetUserIdFromKeyMapByKeyAsync(string key);
        Task<UserKeyMap?> GetUserKeyMapByKeyAsync(string key);
        Task SaveSignUpSettingAsync(TenantConfiguration tenantConfiguration);
        Task<IamTenantConfiguration> GetTenantConfigurationAsync();
        /// <summary>Removes a role slug from the tenant's signup default roles.</summary>
        Task<bool> PullSignUpDefaultRoleAsync(string slug);
        /// <summary>Removes a permission resource from the tenant's signup default permissions.</summary>
        Task<bool> PullSignUpDefaultPermissionAsync(string resource);
        Task SaveOrganizationConfigAsync(IamTenantConfiguration tenantConfiguration);
    }
}

