using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Resources
{
    public class ResourceRepository : IResourceRepository
    {
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;

        public ResourceRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }

        public async Task<Permission> GetPermissionByResourceAsync(string resource, string? organizationId = "default")
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.Resource, resource);
            if (!string.IsNullOrEmpty(organizationId))
            {
                filter = Builders<Permission>.Filter.And(filter, Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId));
            }
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<Permission>> GetPermissionsByResourcesAsync(List<string> resources, string? organizationId = "default")
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.In(x => x.Resource, resources);
            if (!string.IsNullOrEmpty(organizationId))
            {
                filter = Builders<Permission>.Filter.And(filter, Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId));
            }
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<Permission> GetPermissionByIdAsync(string id)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.ItemId, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> InsertPermissionAsync(Permission permission)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            await collection.InsertOneAsync(permission);
            return true;
        }

        public async Task<bool> UpdatePermissionAsync(Permission permission)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == permission.ItemId, permission);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<Role> GetRoleByIdAsync(string id)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.ItemId, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> InsertRoleAsync(Role role)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            await collection.InsertOneAsync(role);
            return true;
        }

        public async Task<bool> UpdateRoleAsync(Role role)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == role.ItemId && x.Slug == role.Slug, role);

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var organizationId = ResolveOrganizationId();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId);

            var permissionCursor = await collection.FindAsync(filter);
            var severityGroups = permissionCursor.ToList().GroupBy(p => p.PermissionSeverity)
                .Select(g => new PermissionGroupBySeverityResponse
                {
                    SeverityLevel =  g.Key.ToString(),
                    Count = g.Count()
                }).ToList();

            return severityGroups;
        }

        public async Task<(IQueryable<Permission>, long)> GetPermissionsAsync(GetPermissionsRequest query)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var organizationId = ResolveOrganizationId();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId);

            SortDefinition<Permission>? sort = null;

            if (query.Filter != null)
            {
                filter &= Builders<Permission>.Filter.Eq(x => x.IsArchived, query.Filter.IsArchived);
                if (query.Filter.Type != ResourceType.None)
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.Type, query.Filter.Type);
                }

                if (query.Filter.PermissionSeverity != PermissionSeverity.None)
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.PermissionSeverity, query.Filter.PermissionSeverity);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.Search))
                {
                    filter &= Builders<Permission>.Filter.Regex(x => x.Name, query.Filter.Search)
                        | Builders<Permission>.Filter.Regex(x => x.Resource, query.Filter.Search)
                        | Builders<Permission>.Filter.Regex(x => x.Description, query.Filter.Search);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.IsBuiltIn))
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.IsBuiltIn, query.Filter.IsBuiltIn.ToLower() == "yes");
                }

                if (query.Filter.Tags.Count > 0)
                {
                    filter &= Builders<Permission>.Filter.AnyIn(x => x.Tags, query.Filter.Tags);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.ResourceGroup))
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.ResourceGroup, query.Filter.ResourceGroup);
                }
                if (query.Filter.Resources.Count > 0)
                {
                    filter &= Builders<Permission>.Filter.In(x => x.Resource, query.Filter.Resources);
                }
            }

            if (query.Sort != null)
            {
                sort = query.Sort.IsDescending ? Builders<Permission>.Sort.Descending(query.Sort.Property) : Builders<Permission>.Sort.Ascending(query.Sort.Property);
            }

            var count = await collection.CountDocumentsAsync(filter);
            var permissions = await collection.Find(filter).Sort(sort).Limit(query.PageSize).Skip(query.PageSize * query.Page).ToListAsync();

            return (permissions.AsQueryable(), count);
        }

        public async Task<ResourceTimeline<T>> GetResourceTimelineAsync<T>(string itemId)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            var result = await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();

            return result;
        }

        public async Task<bool> SaveResourceTimelineAsync<T>(ResourceTimeline<T> timeline)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            var result = await collection.ReplaceOneAsync(x => x.ItemId == timeline.ItemId, timeline, new ReplaceOptions { IsUpsert = true });

            return result.IsAcknowledged;
        }

        public async Task<bool> SaveResourceTimelinesAsync<T>(List<ResourceTimeline<T>> timelines)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            await collection.InsertManyAsync(timelines);

            return true;
        }

        public async Task<Role> GetRoleBySlugAsync(string slug)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var organizationId = ResolveOrganizationId();
            FilterDefinition<Role> filter = Builders<Role>.Filter.Eq("OrganizationId", organizationId);
            filter &= Builders<Role>.Filter.Eq(x => x.Slug, slug);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<(IQueryable<Role>, long)> GetRolesAsync(GetRolesRequest query)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var organizationId = ResolveOrganizationId();

            var filter = Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId);
            SortDefinition<Role>? sort = null;

            if (query.Filter is not null)
            {
                if (!string.IsNullOrWhiteSpace(query.Filter.Search))
                {
                    var regex = new BsonRegularExpression(query.Filter.Search, "i");
                    filter &= Builders<Role>.Filter.Regex(x => x.Name, regex)
                        | Builders<Role>.Filter.Regex(x => x.Description, regex);
                }
                if (query.Filter.Slugs is not null && query.Filter.Slugs.Count > 0)
                    filter &= Builders<Role>.Filter.In(x => x.Slug, query.Filter.Slugs);

            }

            if (query.Sort != null)
            {
                sort = query.Sort.IsDescending ? Builders<Role>.Sort.Descending(query.Sort.Property) :
                                                 Builders<Role>.Sort.Ascending(query.Sort.Property);
            }

            var count = await collection.CountDocumentsAsync(filter);
            var roles = await collection.Find(filter).Sort(sort).Limit(query.PageSize).Skip(query.PageSize * query.Page).ToListAsync();

            return (roles.AsQueryable(), count);
        }

        /// <summary>
        /// Get role by slug and organization. Org-scoped lookup.
        /// </summary>
        public async Task<Role> GetRoleBySlugAndOrgAsync(string slug, string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.Slug, slug) &
                         Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<Role>> GetRolesBySlugAndOrgAsync(List<string> slugs, string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.In(x => x.Slug, slugs) &
                         Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> InsertRolesAsync(List<Role> roles)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            await collection.InsertManyAsync(roles);
            return true;
        }

        /// <summary>
        /// Get all roles in a specific organization.
        /// </summary>
        public async Task<List<Role>> GetRolesByOrgAsync(string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> UpdateRolePermissionByIdsAsync(string slug, List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Permissions");
            var organizationId = ResolveOrganizationId();

            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("OrganizationId", organizationId)
                & Builders<BsonDocument>.Filter.In("ItemId", permissions);

            var update = Builders<BsonDocument>.Update.AddToSet($"Roles", slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> RemoveRolePermissionByIdsAsync(string slug, List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Permissions");
            var organizationId = ResolveOrganizationId();

            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("OrganizationId", organizationId)
                & Builders<BsonDocument>.Filter.In("ItemId", permissions);

            var update = Builders<BsonDocument>.Update.Pull($"Roles", slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> UpdateRolesCountAsync(string slug)
        {
            var organizationId = ResolveOrganizationId();
            var count = await CountRoleUsageAcrossOrganizationAsync(slug, organizationId);

            var update = Builders<Role>.Update.Set(x => x.Count, count);
            var result = await _identityAccessManagementRepository.GetCollection<Role>()
                .UpdateOneAsync(x => x.Slug == slug && x.OrganizationId == organizationId, update);

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var organizationId = ResolveOrganizationId();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
            & (Builders<Permission>.Filter.Ne(x => x.ResourceGroup, null)
            | Builders<Permission>.Filter.Ne(x => x.ResourceGroup, string.Empty));
            var result = await collection.Find(filter).ToListAsync();
            return result.GroupBy(p => p.ResourceGroup).Select(g => new GetResourceGroupResponse
            {
                ResourceGroup = g.Key,
                Count = g.Count()
            }).ToList();
        }

        public async Task<Organization> GetOrganizationById(string id)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            return await  (await collection.FindAsync(Builders<Organization>.Filter.Where(r=>r.ItemId == id))).FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetOrganizationIdsByUserIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var userCollection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Users");
            var filter = Builders<BsonDocument>.Filter.Eq("ItemId", userId);
            var projection = Builders<BsonDocument>.Projection.Include("OrganizationIds");
            var user = await userCollection.Find(filter).Project(projection).FirstOrDefaultAsync();

            if (user == null || !user.TryGetValue("OrganizationIds", out var organizationIdsValue) || !organizationIdsValue.IsBsonArray)
            {
                return [];
            }

            return organizationIdsValue
                .AsBsonArray
                .Where(x => x.IsString)
                .Select(x => x.AsString)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }

        public async Task<List<Organization>> GetOrganizationsByIdsAsync(List<string> organizationIds)
        {
            if (organizationIds == null || organizationIds.Count == 0)
            {
                return [];
            }

            var validOrganizationIds = organizationIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (validOrganizationIds.Count == 0)
            {
                return [];
            }

            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var filter = Builders<Organization>.Filter.In(x => x.ItemId, validOrganizationIds);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task SaveOrganizationAsync(Organization organization)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            await collection.ReplaceOneAsync(r=>r.ItemId == organization.ItemId, organization, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var filter = Builders<Organization>.Filter.Empty;
            

            var totalCount = await collection.CountDocumentsAsync(filter);

            var options = new FindOptions<Organization>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize
            };

            var organizations = await (await collection.FindAsync(filter, options)).ToListAsync();

            return new GetOrganizationsResponse { IsSuccess = true, Organizations = organizations, TotalCount = totalCount };
        }

        public async Task<TenantConfiguration> GetTenantConfigurationAsync()
        {
            return await _identityAccessManagementRepository.GetTenantConfigurationAsync();
        }

        public async Task SaveOrganizationConfig(TenantConfiguration config)
        {
            var collection = _identityAccessManagementRepository.GetCollection<TenantConfiguration>();
            await collection.UpdateOneAsync(
                Builders<TenantConfiguration>.Filter.Empty,
                Builders<TenantConfiguration>.Update
                    .SetOnInsert(c => c.ItemId, config.ItemId)
                    .SetOnInsert(c => c.CreatedBy, config.CreatedBy)
                    .SetOnInsert(c => c.CreatedDate, config.CreatedDate)
                    .Set(c => c.AllowOrgCreationFromCloud, config.AllowOrgCreationFromCloud)
                    .Set(c => c.AllowOrgCreationFromConstruct, config.AllowOrgCreationFromConstruct)
                    .Set(c => c.AllowOrgCreationFromSignup, config.AllowOrgCreationFromSignup)
                    .Set(c => c.AllowOrgCreationFromPortal, config.AllowOrgCreationFromPortal)
                    .Set(c => c.IsMultiOrgEnabled, config.IsMultiOrgEnabled)
                    .Set(c=> c.ConsentForMultiOrgEnable, config.ConsentForMultiOrgEnable)
                    .Set(c => c.LastUpdatedBy, config.LastUpdatedBy)
                    .Set(c => c.LastUpdatedDate, config.LastUpdatedDate), new UpdateOptions { IsUpsert = true });
        }

        private static string ResolveOrganizationId()
        {
            var organizationId = BlocksContext.GetContext()?.OrganizationId;
            return string.IsNullOrWhiteSpace(organizationId) ? "default" : organizationId;
        }


        private async Task<long> CountRoleUsageAcrossOrganizationAsync(string slug, string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Permissions");
            var docs = await collection
                .Find(Builders<BsonDocument>.Filter.Exists("Roles"))
                .Project(Builders<BsonDocument>.Projection.Include("Roles"))
                .ToListAsync();

            var count = 0L;
            foreach (var doc in docs)
            {
                if (!doc.TryGetValue("Roles", out var rolesValue))
                {
                    continue;
                }

                var matched = false;
                if (rolesValue.IsBsonArray)
                {
                    matched = string.Equals(organizationId, "default", StringComparison.OrdinalIgnoreCase)
                        && rolesValue.AsBsonArray.Any(role => role.IsString && string.Equals(role.AsString, slug, StringComparison.OrdinalIgnoreCase));
                }
                else if (rolesValue.IsBsonDocument)
                {
                    if (rolesValue.AsBsonDocument.TryGetValue(organizationId, out var orgRolesValue)
                        && orgRolesValue.IsBsonArray)
                    {
                        matched = orgRolesValue.AsBsonArray.Any(role => role.IsString && string.Equals(role.AsString, slug, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (matched)
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveTenantId()
        {
            return BlocksContext.GetContext()?.TenantId ?? string.Empty;
        }

        public async Task<bool> UpdateAllSamePermissionAsync(Permission permission)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.Resource, permission.Resource);
            var update = Builders<Permission>.Update
                .Set(x => x.Name, permission.Name)
                .Set(x => x.Description, permission.Description)
                .Set(x => x.Type, permission.Type)
                .Set(x => x.PermissionSeverity, permission.PermissionSeverity)
                .Set(x => x.IsArchived, permission.IsArchived)
                .Set(x => x.Tags, permission.Tags)
                .Set(x => x.ResourceGroup, permission.ResourceGroup)
                .Set(x => x.IsBuiltIn, permission.IsBuiltIn)
                .Set(x => x.DependentPermissions, permission.DependentPermissions)
                .Set(x => x.LastUpdatedBy, permission.LastUpdatedBy)
                .Set(x => x.LastUpdatedDate, permission.LastUpdatedDate);

            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<Permission>> GetPermissionsByOrgAsync(string organizationId, int? pageNumber = null, int? pageSize = null)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Permission>.Filter.Eq(x => x.IsArchived, false);

            var query = collection.Find(filter);

            if (pageNumber.HasValue && pageSize.HasValue && pageNumber.Value > 0 && pageSize.Value > 0)
            {
                var skip = (pageNumber.Value - 1) * pageSize.Value;
                query = query.SortBy(x => x.ItemId).Skip(skip).Limit(pageSize.Value);
            }

            return await query.ToListAsync();
        }

        public async Task<bool> AddRoleToPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || resources == null || resources.Count == 0 || string.IsNullOrWhiteSpace(organizationId))
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Permission>.Filter.In(x => x.Resource, resources)
                & Builders<Permission>.Filter.Eq(x => x.IsArchived, false);

            var update = Builders<Permission>.Update.AddToSet(x => x.Roles, slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> RemoveRoleFromPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || resources == null || resources.Count == 0 || string.IsNullOrWhiteSpace(organizationId))
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Permission>.Filter.In(x => x.Resource, resources)
                & Builders<Permission>.Filter.Eq(x => x.IsArchived, false);

            var update = Builders<Permission>.Update.Pull(x => x.Roles, slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<Permission>> GetPermissionsByRoleAsync(string roleSlug, string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");

            var filter = Builders<Permission>.Filter.Eq("OrganizationId", organizationId) &
                        Builders<Permission>.Filter.Eq("Roles", roleSlug)
                        & Builders<Permission>.Filter.Eq("IsArchived", false);

            return await collection.Find(filter).ToListAsync();
        }

        public async Task<List<Permission>> GetFeResourceFeaturesAsync(List<string> roleSlugs, List<string> permissionKeys, string? search = null, bool? isBuiltIn = null)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");
            var organizationId = ResolveOrganizationId();

            var normalizedRoleSlugs = (roleSlugs ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var normalizedPermissionKeys = (permissionKeys ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (!normalizedRoleSlugs.Any() && !normalizedPermissionKeys.Any())
            {
                return [];
            }

            var accessFilters = new List<FilterDefinition<Permission>>();

            if (normalizedRoleSlugs.Any())
            {
                accessFilters.Add(Builders<Permission>.Filter.AnyIn(x => x.Roles, normalizedRoleSlugs));
            }

            if (normalizedPermissionKeys.Any())
            {
                accessFilters.Add(
                    Builders<Permission>.Filter.In(x => x.Resource, normalizedPermissionKeys)
                    | Builders<Permission>.Filter.In(x => x.ItemId, normalizedPermissionKeys)
                );
            }

            var accessFilter = accessFilters.Count == 1
                ? accessFilters[0]
                : Builders<Permission>.Filter.Or(accessFilters);

            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Permission>.Filter.Eq(x => x.IsArchived, false)
                & Builders<Permission>.Filter.Eq(x => x.Type, ResourceType.FrontendAction)
                & accessFilter;

            if (isBuiltIn.HasValue)
            {
                filter &= Builders<Permission>.Filter.Eq(x => x.IsBuiltIn, isBuiltIn.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var regex = new BsonRegularExpression(search, "i");
                var searchFilter = Builders<Permission>.Filter.Regex(x => x.Resource, regex)
                    | Builders<Permission>.Filter.Regex(x => x.Name, regex)
                    | Builders<Permission>.Filter.Regex(x => x.Description, regex);

                filter &= searchFilter;
            }

            return await collection.Find(filter).SortBy(x => x.Name).ToListAsync();
        }


        public async Task<List<Permission>> GetPermissionsByRolesAsync(List<string> roleSlugs, string organizationId, int pageNumber = 1, int pageSize = 10)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");

            var filter = Builders<Permission>.Filter.Eq("OrganizationId", organizationId) &
                        Builders<Permission>.Filter.In("Roles", roleSlugs) &
                        Builders<Permission>.Filter.Eq("IsArchived", false);

            var skip = (pageNumber - 1) * pageSize;
            return await collection.Find(filter).Skip(skip).Limit(pageSize).ToListAsync();
        }

        public async Task<List<Permission>> GetPermissionsByGroupsAsync(List<string> groups, string organizationId, int pageNumber = 1, int pageSize = 10)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");

            var filter = Builders<Permission>.Filter.Eq("OrganizationId", organizationId) &
                        Builders<Permission>.Filter.In("ResourceGroups", groups) &
                        Builders<Permission>.Filter.Eq("IsArchived", false);

            var skip = (pageNumber - 1) * pageSize;
            return await collection.Find(filter).Skip(skip).Limit(pageSize).ToListAsync();
        }

        public async Task<List<Permission>> GetPermissionsByIdsAsync(List<string> ids)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");

            var filter = Builders<Permission>.Filter.In("ItemId", ids) &
                        Builders<Permission>.Filter.Eq("IsArchived", false);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> InsertPermissionsAsync(List<Permission> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            await collection.InsertManyAsync(permissions);
            return true;
        }


        public async Task<List<Permission>> GetPermissionsByResourceAsync(string resource)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");

            var filter = Builders<Permission>.Filter.Eq(x => x.Resource, resource);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> UpdatePermissionsAsync(List<Permission> permissions)
        {
            if (permissions == null || permissions.Count == 0)
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();

            var operations = permissions
                .Select(permission =>
                    new ReplaceOneModel<Permission>(
                        Builders<Permission>.Filter.Eq(x => x.ItemId, permission.ItemId),
                        permission)
                    {
                        IsUpsert = false
                    })
                .Cast<WriteModel<Permission>>()
                .ToList();

            var result = await collection.BulkWriteAsync(
                operations,
                new BulkWriteOptions
                {
                    IsOrdered = false
                });

            return result.IsAcknowledged;
        }

        public async Task<List<Role>> GetRolesBySlugAsync(string slug)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Role>("Roles");

            var filter = Builders<Role>.Filter.Eq(x => x.Slug, slug);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> UpdateRolesAsync(List<Role> roles)
        {
            if (roles == null || roles.Count == 0)
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Role>();

            var operations = roles
                .Select(role =>
                    new ReplaceOneModel<Role>(
                        Builders<Role>.Filter.Eq(x => x.ItemId, role.ItemId),
                        role)
                    {
                        IsUpsert = false
                    })
                .Cast<WriteModel<Role>>()
                .ToList();

            var result = await collection.BulkWriteAsync(
                operations,
                new BulkWriteOptions
                {
                    IsOrdered = false
                });

            return result.IsAcknowledged;
        }

    }
}
