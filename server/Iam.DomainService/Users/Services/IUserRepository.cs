using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Users
{
    public interface IUserRepository
    {
        Task<User> GetUserByUserNameOrgIdAsync(string userName, string organizatoinId = "");
        Task<bool> CheckPasswordBlackListedAsync(string password);
        Task<User> GetUserByEmailAsync(string email);
        Task<bool> CreateUserAsync(User user);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<bool> UpdateUserAsync(User user);

        /// <summary>
        /// Atomically records one successful login. See
        /// <see cref="Iam.DomainService.Services.IIdentityAccessManagementRepository.RecordSuccessfulLoginAsync"/>.
        /// </summary>
        Task<bool> RecordSuccessfulLoginAsync(string userId, string deviceInformationJson, DateTime nowUtc);
        Task<(IQueryable<T>?, long)> GetUsersAsync<T, R>(R query, UserListScope scope) where R : BaseGetsRequest<GetUsersFilter>;
        /// <summary>As the caller may see them: properties a View column rule hides are cleared. Never save them back.</summary>
        Task<(IQueryable<T>?, long)> GetUsersForViewAsync<T, R>(R query, UserListScope scope) where R : BaseGetsRequest<GetUsersFilter>;
        Task<IamConfiguration> GetIamConfigurationAsync();
        Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap);
        Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(string id);
        Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(List<string> permissions);
        Task<List<GetUserPermission>> GetPermissionsByRolesAsync(List<string> roles);
        Task<List<GetUserRole>> GetRolesBySlugsAsync(string id);
        Task<List<GetUserRole>> GetRolesBySlugsAsync(List<string> roles);
        Task<string> GetProjectIdFromProjectPeopleAsync(string userId);

        /// <summary>
        /// The accounts of these emails, matched as <see cref="GetUserByEmailAsync"/> matches one: one <c>$in</c> read.
        /// Throws <see cref="UnauthorizedAccessException"/> when the read is denied rather than answering "none".
        /// </summary>
        Task<List<User>> GetUsersByEmailsAsync(IReadOnlyCollection<string> emails);

        /// <summary>The accounts with these ids: one <c>$in</c> read.</summary>
        Task<List<User>> GetUsersByIdsAsync(IReadOnlyCollection<string> userIds);

        /// <summary>
        /// One unordered bulk write: new accounts inserted, existing ones updated by id. A user whose write
        /// fails is reported by id and the others are still written.
        /// </summary>
        Task<UserBulkWriteResult> BulkWriteUsersAsync(IReadOnlyList<User> inserts, IReadOnlyList<UserBulkUpdate> updates);
    }

    /// <summary>A partial update of one existing account in <see cref="IUserRepository.BulkWriteUsersAsync"/>.</summary>
    public sealed record UserBulkUpdate(string UserId, MongoDB.Driver.UpdateDefinition<User> Update);

    /// <summary>Users a bulk write did not write: refused by a unique index, or failed for any other reason.</summary>
    public sealed record UserBulkWriteResult(IReadOnlySet<string> DuplicateUserIds, IReadOnlySet<string> FailedUserIds);
}

