using Blocks.Genesis.Entities.Data;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using MongoDB.Driver;

namespace Iam.DomainService.AccessGrant
{
    /// <summary>The reads access grant needs, so the service itself holds no collection logic.</summary>
    public interface IAccessGrantRepository
    {
        Task<User?> GetUserAsync(string userId);

        /// <summary>Non-archived roles of one organization.</summary>
        Task<List<Role>> GetRolesAsync(string organizationId);

        /// <summary>Non-archived permissions of one organization.</summary>
        Task<List<Permission>> GetPermissionsAsync(string organizationId);

        /// <summary>
        /// Role slugs named by a live data-access policy as an Auth <c>Roles</c> rule against a
        /// static value, in the tenant's <c>DataAccessPolicys</c> collection.
        /// </summary>
        Task<HashSet<string>> GetPolicyRoleSlugsAsync();

        /// <summary>Ids of every user holding the role in the organization.</summary>
        Task<List<string>> GetUserIdsWithRoleAsync(string slug, string organizationId);
    }

    public sealed class AccessGrantRepository : IAccessGrantRepository
    {
        /// <summary>Genesis collection naming: the entity name plus "s", as blocks-data writes it.</summary>
        private const string DataAccessPolicyCollectionName = "DataAccessPolicys";
        private const string RolesOperand = "Roles";

        /// <summary>Bounded like the role-hierarchy walk: policies are user data, and a group nests itself only by mistake.</summary>
        private const int MaxRuleGroupDepth = 32;

        private readonly IResourceRepository _resourceRepository;
        private readonly IUserRepository _userRepository;
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;
        private readonly IIamDataAccess _dataAccess;

        public AccessGrantRepository(
            IResourceRepository resourceRepository,
            IUserRepository userRepository,
            IIdentityAccessManagementRepository identityAccessManagementRepository,
            IIamDataAccess dataAccess)
        {
            _resourceRepository = resourceRepository;
            _userRepository = userRepository;
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _dataAccess = dataAccess;
        }

        public async Task<User?> GetUserAsync(string userId) =>
            string.IsNullOrWhiteSpace(userId) ? null : await _userRepository.GetUserByIdAsync(userId).ConfigureAwait(false);

        public async Task<List<Role>> GetRolesAsync(string organizationId) =>
            await _resourceRepository.GetRolesByOrgAsync(organizationId).ConfigureAwait(false) ?? [];

        public async Task<List<Permission>> GetPermissionsAsync(string organizationId)
        {
            // Ne(..., true), not Eq(..., false): a permission document written before IsArchived
            // existed has no such field and is live. Missing it here would shrink the caller's
            // power and refuse grants it is entitled to.
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Permission>.Filter.Ne(x => x.IsArchived, true);
            var scope = await _dataAccess
                .ReadAsync(IamSchemas.Permission, filter, "AccessGrantRepository.GetPermissionsAsync", useProjection: false)
                .ConfigureAwait(false);

            return scope.Granted ? await scope.Apply(collection).ToListAsync().ConfigureAwait(false) : [];
        }

        public async Task<HashSet<string>> GetPolicyRoleSlugsAsync()
        {
            // DataAccessPolicy is a blocks-data entity, not one iam governs, so it is read directly:
            // the data-access resolver has no schema for it and would leave the call untouched anyway.
            var collection = _identityAccessManagementRepository.GetCollectionByName<DataAccessPolicy>(DataAccessPolicyCollectionName);
            var filter = Builders<DataAccessPolicy>.Filter.Ne(x => x.IsDeleted, true);
            var policies = await collection
                .Find(filter)
                .Project(x => x.RuleGroup)
                .ToListAsync()
                .ConfigureAwait(false);

            var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in policies)
            {
                CollectRoleSlugs(group, slugs, depth: 0);
            }

            return slugs;
        }

        public async Task<List<string>> GetUserIdsWithRoleAsync(string slug, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(organizationId))
            {
                return [];
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();
            var filter = Builders<User>.Filter.AnyEq($"Roles.{organizationId}", slug);
            var scope = await _dataAccess
                .ReadAsync(IamSchemas.User, filter, "AccessGrantRepository.GetUserIdsWithRoleAsync", useProjection: false)
                .ConfigureAwait(false);
            if (!scope.Granted)
            {
                return [];
            }

            return await scope.Apply(collection).Project(x => x.ItemId).ToListAsync().ConfigureAwait(false);
        }

        private static void CollectRoleSlugs(PolicyRuleGroup? group, HashSet<string> slugs, int depth)
        {
            if (group is null || depth > MaxRuleGroupDepth)
            {
                return;
            }

            foreach (var rule in group.Rules ?? [])
            {
                AddRoleValues(rule, slugs);
            }

            foreach (var nested in group.NestedGroups ?? [])
            {
                CollectRoleSlugs(nested, slugs, depth + 1);
            }
        }

        private static void AddRoleValues(PolicyRule? rule, HashSet<string> slugs)
        {
            if (rule is null)
            {
                return;
            }

            // Written either way round: Auth.Roles == "x", or "x" == Auth.Roles.
            var authLeft = rule.LeftSource == ConditionSource.AUTH
                && string.Equals(rule.LeftOperand, RolesOperand, StringComparison.OrdinalIgnoreCase)
                && rule.RightSource == ConditionSource.STATIC_VALUE;
            var authRight = rule.RightSource == ConditionSource.AUTH
                && string.Equals(rule.RightOperand, RolesOperand, StringComparison.OrdinalIgnoreCase)
                && rule.LeftSource == ConditionSource.STATIC_VALUE;

            if (!authLeft && !authRight)
            {
                return;
            }

            switch (rule.StaticValue)
            {
                case string slug when !string.IsNullOrWhiteSpace(slug):
                    slugs.Add(slug);
                    break;
                case System.Collections.IEnumerable values and not string:
                    foreach (var value in values.OfType<string>().Where(v => !string.IsNullOrWhiteSpace(v)))
                    {
                        slugs.Add(value);
                    }

                    break;
            }
        }
    }
}
