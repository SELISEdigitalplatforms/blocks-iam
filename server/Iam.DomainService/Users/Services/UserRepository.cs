using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace Iam.DomainService.Users
{
    public class UserRepository : IUserRepository
    {
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;

        public UserRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }

        public async Task<bool> CheckPasswordBlackListedAsync(string password)
        {
            return await _identityAccessManagementRepository.CheckPasswordBlackListedAsync(password);
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            NormalizeUserIdentity(user);
            var collection = _identityAccessManagementRepository.GetCollection<User>();
            await collection.InsertOneAsync(user);

            return true;
        }

        public async Task<IamConfiguration> GetIamConfigurationAsync()
        {
            return await _identityAccessManagementRepository.GetIamConfigurationAsync();
        }

        public async Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(string id)
        {
            var user = await _identityAccessManagementRepository.GetCollection<User>().Find(x => x.ItemId == id).FirstOrDefaultAsync();
            if (user == null || user.Permissions.Count == 0) return [];
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var project = Builders<Permission>.Projection.As<GetUserPermission>();
            var permissions = GetOrgSpecificPermissions(user);
            var filter = Builders<Permission>.Filter.In(x => x.Resource, permissions.ToList());
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var project = Builders<Permission>.Projection.As<GetUserPermission>();
            var filter = Builders<Permission>.Filter.In(x => x.Resource, permissions);
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserPermission>> GetPermissionsByRolesAsync(List<string> roles)
        {
            if (roles.Count == 0)
            {
                return [];
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var project = Builders<Permission>.Projection.As<GetUserPermission>();
            var organizationId = ResolvePermissionOrganizationId();
            var orgFilter = Builders<Permission>.Filter.AnyIn($"Roles.{organizationId}", roles);
            var filter = Builders<Permission>.Filter.Or(
                orgFilter,
                organizationId == "default"
                    ? Builders<Permission>.Filter.AnyIn("Roles.default", roles)
                    : Builders<Permission>.Filter.Where(_ => false));
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserRole>> GetRolesBySlugsAsync(string id)
        {
            var user = await _identityAccessManagementRepository.GetCollection<User>().Find(x => x.ItemId == id).FirstOrDefaultAsync();
            if (user == null || user.Roles.Count == 0) return [];
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var project = Builders<Role>.Projection.As<GetUserRole>();
            var context = BlocksContext.GetContext();
            var orgId = string.IsNullOrWhiteSpace(context?.OrganizationId) ? "default" : context.OrganizationId;
            var tenantId = context?.TenantId;

            var filter = Builders<Role>.Filter.In(x => x.Slug, GetOrgSpecficRoles(user))
                & Builders<Role>.Filter.Eq(x => x.OrganizationId, orgId);

            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserRole>> GetRolesBySlugsAsync(List<string> roles)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var project = Builders<Role>.Projection.As<GetUserRole>();
            var context = BlocksContext.GetContext();
            var orgId = string.IsNullOrWhiteSpace(context?.OrganizationId) ? "default" : context.OrganizationId;
            var tenantId = context?.TenantId;

            var filter = Builders<Role>.Filter.In(x => x.Slug, roles)
                & Builders<Role>.Filter.Eq(x => x.OrganizationId, orgId);

            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _identityAccessManagementRepository.GetUserByEmailAsync(NormalizeEmail(email));
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            return await _identityAccessManagementRepository.GetUserByIdAsync(itemId);
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            return await _identityAccessManagementRepository.GetUserByIdAsync<T>(itemId);
        }

        public async Task<User> GetUserByUserNameOrgIdAsync(string userName, string organizatoinId = "")
        {
            var collection = _identityAccessManagementRepository.GetCollection<User>();
            userName = NormalizeIdentity(userName);
            var options = new FindOptions
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };

            var user = !string.IsNullOrWhiteSpace(organizatoinId)
                ? await collection.Find(x => x.UserName == userName && x.OrganizationIds.Any(o => o == organizatoinId), options).FirstOrDefaultAsync()
                : await collection.Find(x => x.UserName == userName, options).FirstOrDefaultAsync();

            return user;
        }

        public async Task<(IQueryable<T>?, long)> GetUsersAsync<T, R>(R query, UserListScope scope) where R : BaseGetsRequest<GetUsersFilter>
        {
            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var filter = BuildUserFilter(query.Filter, scope);
            var sort = BuildSortDefinition(query.Sort);
            var projection = Builders<User>.Projection.As<T>();

            var totalCount = await collection.CountDocumentsAsync(filter);

            var options = new FindOptions<User, T>
            {
                Skip = query.PageSize * query.Page,
                Limit = query.PageSize,
                Sort = sort,
                Projection = projection
            };

            var cursor = await collection.FindAsync(filter, options);
            var data = await cursor.ToListAsync();

            return (data.AsQueryable(), totalCount);
        }

        /// <summary>
        /// Build the query for an already-resolved scope. The scope is decided by
        /// <see cref="UserListOrganizationScope"/> upstream rather than here, so this stays a pure
        /// translation of a decision into a filter and never re-reads the ambient context.
        /// </summary>
        private static FilterDefinition<User> BuildUserFilter(GetUsersFilter? filter, UserListScope scope)
        {
            var builder = Builders<User>.Filter;
            var filters = new List<FilterDefinition<User>>();

            // AnyIn for every non-empty scope, single id included: one code path, and on an array
            // field it means "belongs to any of these", which is the union the caller asked for.
            if (scope.Kind == UserListScopeKind.Organizations)
            {
                filters.Add(builder.AnyIn(x => x.OrganizationIds, scope.OrganizationIds));
            }

            if (filter == null)
            {
                // The organization clause is no longer guaranteed, so this early return needs the
                // same emptiness guard as the one at the end of the method: a tenant-wide scope with
                // no filter leaves nothing to conjoin.
                return filters.Any() ? builder.And(filters) : builder.Empty;
            }

            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                // searchTerm stays unescaped: the Contains below is a literal
                // comparison, so backslashes would have to be typed to match.
                // Only the regex form of the term is escaped.
                var searchTerm = filter.Name.Trim().ToLower();
                var regex = new BsonRegularExpression(Regex.Escape(searchTerm), "i");

                var orFilters = new List<FilterDefinition<User>>
                {
                  builder.Regex(u => u.FirstName, regex),
                  builder.Regex(u => u.LastName, regex),
                  builder.Where(u => (u.FirstName + " " + u.LastName).ToLower().Contains(searchTerm))
                };

                filters.Add(builder.Or(orFilters));
            }

            if (!string.IsNullOrWhiteSpace(filter.Email))
            {
                // Substring match, so a partial address finds a user the same way
                // the Name filter above already does. Exact-match email lookups
                // (GetUserByEmailAsync, login) go through a different path and are
                // unaffected.
                var emailTerm = NormalizeEmail(filter.Email);
                filters.Add(builder.Regex(u => u.Email, new BsonRegularExpression(Regex.Escape(emailTerm), "i")));
            }

            if (filter.Status?.Active == true)
                filters.Add(builder.Eq(u => u.Active, true));

            if (filter.Status?.Inactive == true)
                filters.Add(builder.Eq(u => u.Active, false));

            if (filter.Mfa?.Enabled == true)
                filters.Add(builder.Eq(u => u.MfaEnabled, true));

            if (filter.Mfa?.Disabled == true)
                filters.Add(builder.Eq(u => u.MfaEnabled, false));

            if (filter.JoinedOn.HasValue)
                filters.Add(builder.Gte(u => u.CreatedDate, filter.JoinedOn.Value.Date));

            if (filter.LastLogin.HasValue)
                filters.Add(builder.Gte(u => u.LastLoggedInTime, filter.LastLogin.Value.Date));

            if (filter.UserIds is not null && filter.UserIds.Count > 0)
                filters.Add(builder.In(u => u.ItemId, filter.UserIds));

            return filters.Any() ? builder.And(filters) : builder.Empty;
        }

        private static SortDefinition<User> BuildSortDefinition(BaseSortRequest? sortRequest)
        {
            var builder = Builders<User>.Sort;

            if (sortRequest == null || string.IsNullOrWhiteSpace(sortRequest.Property))
                return builder.Descending(u => u.CreatedDate);

            return sortRequest.IsDescending
                ? builder.Descending(sortRequest.Property)
                : builder.Ascending(sortRequest.Property);
        }

        public async Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap)
        {
            return await _identityAccessManagementRepository.InsertUserKeyMapAsync(userKeyMap);
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            NormalizeUserIdentity(user);
            return await _identityAccessManagementRepository.UpdateUserAsync(user);
        }

        public async Task<string> GetProjectIdFromProjectPeopleAsync(string userId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<ProjectPeople>();
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId);
            return (await collection.FindAsync(filter)).FirstOrDefault().TenantId;
        }

        private static List<string> GetOrgSpecficRoles(User user)
        {
            var orgId = BlocksContext.GetContext()?.OrganizationId;
            if (!string.IsNullOrWhiteSpace(orgId) && user.Roles.TryGetValue(orgId, out var rolesByOrg))
            {
                return rolesByOrg ?? [];
            }

            if (user.Roles.TryGetValue("default", out var defaultRoles))
            {
                return defaultRoles ?? [];
            }

            return user.Roles.Values.FirstOrDefault() ?? [];
        }

        private static List<string> GetOrgSpecificPermissions(User user)
        {
            var orgId = BlocksContext.GetContext()?.OrganizationId;
            if (!string.IsNullOrWhiteSpace(orgId) && user.Permissions.TryGetValue(orgId, out var permissionsByOrg))
            {
                return permissionsByOrg ?? [];
            }

            if (user.Permissions.TryGetValue("default", out var defaultPermissions))
            {
                return defaultPermissions ?? [];
            }

            return user.Permissions.Values.FirstOrDefault() ?? [];
        }

        private static string ResolvePermissionOrganizationId()
        {
            var orgId = BlocksContext.GetContext()?.OrganizationId;
            return string.IsNullOrWhiteSpace(orgId) ? "default" : orgId;
        }

        private static void NormalizeUserIdentity(User user)
        {
            user.Email = NormalizeEmail(user.Email);
            user.UserName = NormalizeIdentity(user.UserName);
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }

        private static string NormalizeIdentity(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

    }
}
