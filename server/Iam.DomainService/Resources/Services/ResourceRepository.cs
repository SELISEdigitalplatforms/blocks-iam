using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

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

        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync(string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var resolvedOrgId = ResolveOrganizationId(organizationId);
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, resolvedOrgId);

            var permissionCursor = await collection.FindAsync(filter);
            var severityGroups = permissionCursor.ToList().GroupBy(p => p.PermissionSeverity)
                .Select(g => new PermissionGroupBySeverityResponse
                {
                    SeverityLevel =  g.Key.ToString(),
                    Count = g.Count()
                }).ToList();

            return severityGroups;
        }

        public async Task<(IQueryable<Permission>, long)> GetPermissionsAsync(GetPermissionsRequest query, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var resolvedOrgId = ResolveOrganizationId(organizationId);
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, resolvedOrgId);

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

            if (query.Roles != null && query.Roles.Count > 0)
            {
                var normalizedRoles = query.Roles
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (normalizedRoles.Count > 0)
                {
                    filter &= Builders<Permission>.Filter.AnyIn(x => x.Roles, normalizedRoles);
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

        public async Task<Role> GetRoleBySlugAsync(string slug, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var resolvedOrgId = ResolveOrganizationId(organizationId);
            FilterDefinition<Role> filter = Builders<Role>.Filter.Eq("OrganizationId", resolvedOrgId);
            filter &= Builders<Role>.Filter.Eq(x => x.Slug, slug);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<(IQueryable<Role>, long)> GetRolesAsync(GetRolesRequest query, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var resolvedOrgId = ResolveOrganizationId(organizationId);

            // Ne(..., true), never Eq(..., false): IsArchived is newer than the role documents, and
            // a missing field does not match false. Eq would return nothing for pre-existing roles
            // and empty this list -- and GetAssignableRolesAsync with it -- for every tenant.
            var filter = Builders<Role>.Filter.Eq(x => x.OrganizationId, resolvedOrgId)
                & Builders<Role>.Filter.Ne(x => x.IsArchived, true);
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
            // Archived roles are excluded here too, because this feeds CopyRoleFromDefault: without
            // it, archiving a default-organization role would still clone it into every
            // organization provisioned afterwards, resurrecting it unarchived. Same Ne(..., true)
            // reasoning as GetRolesAsync.
            var filter = Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Role>.Filter.Ne(x => x.IsArchived, true);
            return await collection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// True when any role in the organization names this slug as its parent. Archived children
        /// do not count: archiving is a soft delete, so an archived child keeps its ParentRoleSlug,
        /// and counting it would make a parent permanently unarchivable once its children are gone.
        /// </summary>
        public async Task<bool> HasChildRolesAsync(string slug, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.OrganizationId, organizationId)
                & Builders<Role>.Filter.Eq(x => x.ParentRoleSlug, slug)
                & Builders<Role>.Filter.Ne(x => x.IsArchived, true);

            return await collection.CountDocumentsAsync(filter) > 0;
        }

        /// <summary>
        /// True when a genuinely active user in the organization still holds this role.
        /// </summary>
        /// <remarks>
        /// A missing Status is treated as Active, matching both the C# initialiser on
        /// <see cref="User.Status"/> and the in-memory predicate used elsewhere for "truly active".
        /// Status was added after user documents already existed, and this is the first
        /// database-level filter on it, so Eq alone would let a legacy holder slip through and be
        /// silently scrubbed instead of blocking the archive. A missing Active needs no such
        /// handling: it deserialises to false, which already excludes the user.
        /// </remarks>
        public async Task<bool> HasUserAssignmentsAsync(string slug, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();
            var filter = Builders<User>.Filter.AnyEq($"Roles.{organizationId}", slug)
                & Builders<User>.Filter.Eq(x => x.Active, true)
                & (Builders<User>.Filter.Eq(x => x.Status, UserLifecycleStatus.Active)
                    | Builders<User>.Filter.Exists(x => x.Status, false));

            return await collection.CountDocumentsAsync(filter) > 0;
        }

        /// <summary>
        /// Removes the slug from every permission in the organization that references it. Unlike
        /// <see cref="RemoveRoleFromPermissionsByResourcesAsync"/> this is not scoped to a list of
        /// resources, and it deliberately does not skip archived permissions: the invariant wanted
        /// is that no permission in the organization still names an archived role.
        /// </summary>
        public async Task<bool> RemoveRoleFromAllPermissionsAsync(string slug, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(organizationId))
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.AnyEq(x => x.Roles, slug)
                & Builders<Permission>.Filter.Eq(x => x.OrganizationId, organizationId);

            var update = Builders<Permission>.Update.Pull(x => x.Roles, slug);
            var result = await collection.UpdateManyAsync(filter, update);

            // IsAcknowledged, not ModifiedCount > 0: a role referenced by no permission matches
            // nothing, which is an acknowledged write of zero documents and a perfectly normal
            // archive, not a failure.
            return result?.IsAcknowledged ?? false;
        }

        /// <summary>
        /// Removes the slug from the given organization's bucket in every user holding it.
        /// </summary>
        /// <remarks>
        /// User.Roles is a Dictionary&lt;string, List&lt;string&gt;&gt;, which BSON-serialises as a
        /// subdocument, so the bucket is addressed by the dotted path Roles.{organizationId} and
        /// filter and update must name the same path. Scoping matters: the same slug under a
        /// different organization key belongs to that organization's copy of the role and is left
        /// alone.
        /// </remarks>
        public async Task<bool> RemoveRoleFromAllUsersAsync(string slug, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(organizationId))
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();
            var orgBucket = $"Roles.{organizationId}";

            var filter = Builders<User>.Filter.AnyEq(orgBucket, slug);
            var update = Builders<User>.Update.Pull(orgBucket, slug);
            var result = await collection.UpdateManyAsync(filter, update);

            return result?.IsAcknowledged ?? false;
        }

        /// <summary>
        /// Removes the permission resource from the given organization's bucket in every user
        /// holding it directly.
        /// </summary>
        /// <remarks>
        /// The permission-side twin of <see cref="RemoveRoleFromAllUsersAsync"/>, and the one that
        /// actually matters for access: <c>User.Permissions[orgId]</c> is what
        /// AuthorizationClaimsResolver reads to mint permission claims, whereas the
        /// <c>Permission.Roles</c> array the archive already cleans grants nothing by itself.
        /// Same dotted-path mechanics -- <c>User.Permissions</c> is a
        /// Dictionary&lt;string, List&lt;string&gt;&gt; and BSON-serialises as a subdocument, so
        /// filter and update must name the same path. Scoped to one organization: the same
        /// resource under a different organization key belongs to that organization's copy.
        /// </remarks>
        public async Task<bool> RemovePermissionFromAllUsersAsync(string resource, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(organizationId))
            {
                return true;
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();
            var orgBucket = $"Permissions.{organizationId}";

            var filter = Builders<User>.Filter.AnyEq(orgBucket, resource);
            var update = Builders<User>.Update.Pull(orgBucket, resource);
            var result = await collection.UpdateManyAsync(filter, update);

            // IsAcknowledged, not ModifiedCount > 0: a permission nobody holds directly matches
            // nothing, which is an acknowledged write of zero documents and a normal archive.
            return result?.IsAcknowledged ?? false;
        }

        /// <summary>
        /// Counts DISTINCT users holding this role slug in ANY of the given organizations.
        /// </summary>
        /// <remarks>
        /// Distinct matters: a user can hold the same slug under several organization keys, and the
        /// archive dialog reports "how many people lose this", not "how many assignments vanish".
        /// One query with an Or over the org buckets does the de-duplication in the server rather
        /// than summing per-organization counts, which would double-count that user.
        /// When <paramref name="activeOnly"/> is set the predicate is the SAME one
        /// <see cref="HasUserAssignmentsAsync"/> uses -- including treating a missing Status as
        /// Active -- so the preview can never disagree with the guard it is previewing.
        /// </remarks>
        public async Task<long> CountUsersWithRoleAsync(string slug, IEnumerable<string> organizationIds, bool activeOnly)
        {
            var orgIds = organizationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? [];

            if (string.IsNullOrWhiteSpace(slug) || orgIds.Count == 0)
            {
                return 0;
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var filter = Builders<User>.Filter.Or(
                orgIds.Select(orgId => Builders<User>.Filter.AnyEq($"Roles.{orgId}", slug)));

            if (activeOnly)
            {
                filter &= Builders<User>.Filter.Eq(x => x.Active, true)
                    & (Builders<User>.Filter.Eq(x => x.Status, UserLifecycleStatus.Active)
                        | Builders<User>.Filter.Exists(x => x.Status, false));
            }

            return await collection.CountDocumentsAsync(filter);
        }

        /// <summary>
        /// Counts DISTINCT users holding this permission resource DIRECTLY in any of the given
        /// organizations.
        /// </summary>
        /// <remarks>
        /// This reads <c>User.Permissions</c>, the per-user grant dictionary -- NOT
        /// <c>Permission.Roles</c>. The two are unrelated grant paths and both have to be reported:
        /// only this one is read by AuthorizationClaimsResolver when minting the permission claims
        /// into an access token, so it is the binding that actually decides access.
        /// No active-only variant exists because the permission archive has no active-user guard to
        /// mirror.
        /// </remarks>
        public async Task<long> CountUsersWithPermissionAsync(string resource, IEnumerable<string> organizationIds)
        {
            var orgIds = organizationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? [];

            if (string.IsNullOrWhiteSpace(resource) || orgIds.Count == 0)
            {
                return 0;
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var filter = Builders<User>.Filter.Or(
                orgIds.Select(orgId => Builders<User>.Filter.AnyEq($"Permissions.{orgId}", resource)));

            return await collection.CountDocumentsAsync(filter);
        }

        /// <summary>
        /// Every non-archived role carrying this slug, across all organizations.
        /// </summary>
        /// <remarks>
        /// Deliberately distinct from <see cref="GetRolesBySlugAsync"/>, which returns archived
        /// copies too and must keep doing so -- InsertRoleForAllOrg relies on finding an archived
        /// copy to avoid creating a duplicate alongside it. The preview needs the opposite: an
        /// already-archived copy is one the archive will skip, so counting it would overstate the
        /// blast radius. Ne(..., true) rather than Eq(..., false) because IsArchived is newer than
        /// the role documents and is absent from every pre-existing one.
        /// </remarks>
        public async Task<List<Role>> GetNonArchivedRolesBySlugAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return [];
            }

            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.Slug, slug)
                & Builders<Role>.Filter.Ne(x => x.IsArchived, true);

            return await collection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// Counts DISTINCT role slugs referencing this permission resource across the given
        /// organizations.
        /// </summary>
        public async Task<long> CountRoleBindingsForResourceAsync(string resource, IEnumerable<string> organizationIds)
        {
            var orgIds = organizationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? [];

            if (string.IsNullOrWhiteSpace(resource) || orgIds.Count == 0)
            {
                return 0;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.Resource, resource)
                & Builders<Permission>.Filter.In(x => x.OrganizationId, orgIds);

            var permissions = await collection.Find(filter).Project(x => x.Roles).ToListAsync();

            // Counted in memory: the same slug appears in every organization's copy of the
            // permission, and the answer wanted is "how many distinct roles reference this", not
            // "how many documents mention it".
            return permissions
                .Where(x => x != null)
                .SelectMany(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .LongCount();
        }

        public async Task<bool> UpdateRolePermissionByIdsAsync(string slug, List<string> permissions, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Permissions");
            var resolvedOrgId = ResolveOrganizationId(organizationId);

            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("OrganizationId", resolvedOrgId)
                & Builders<BsonDocument>.Filter.In("_id", permissions);

            var update = Builders<BsonDocument>.Update.AddToSet($"Roles", slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> RemoveRolePermissionByIdsAsync(string slug, List<string> permissions, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Permissions");
            var resolvedOrgId = ResolveOrganizationId(organizationId);

            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("OrganizationId", resolvedOrgId)
                & Builders<BsonDocument>.Filter.In("_id", permissions);

            var update = Builders<BsonDocument>.Update.Pull($"Roles", slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> UpdateRolesCountAsync(string slug, string? organizationId = null)
        {
            var resolvedOrgId = ResolveOrganizationId(organizationId);
            long count;
            try
            {
                count = await CountRoleUsageAcrossOrganizationAsync(slug, resolvedOrgId);
            }
            catch
            {
                count = 0;
            }

            var update = Builders<Role>.Update.Set(x => x.Count, count);
            var result = await _identityAccessManagementRepository.GetCollection<Role>()
                .UpdateOneAsync(x => x.Slug == slug && x.OrganizationId == resolvedOrgId, update);

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync(string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var resolvedOrgId = ResolveOrganizationId(organizationId);
            var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, resolvedOrgId)
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

        public async Task<Organization> GetOrganizationByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var normalizedName = name.Trim();
            var escapedName = Regex.Escape(normalizedName);
            var regex = new BsonRegularExpression($"^{escapedName}$", "i");
            var filter = Builders<Organization>.Filter.Regex(x => x.Name, regex);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetOrganizationIdsByUserIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var filter = Builders<User>.Filter.Eq(x => x.ItemId, userId);

            var organizationIds = await collection
                .Find(filter)
                .Project(x => x.OrganizationIds)
                .FirstOrDefaultAsync();

            return organizationIds?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList() ?? [];
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

        public async Task DeleteOrganizationAsync(string organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return;
            }

            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            await collection.DeleteOneAsync(r => r.ItemId == organizationId);
        }

        public async Task<List<string>> GetAllOrgIdsAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var orgIds = await collection.Find(Builders<Organization>.Filter.Empty).Project(x => x.ItemId).ToListAsync();

            return orgIds;
        }

        public async Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var filter = Builders<Organization>.Filter.Empty;

            SortDefinition<Organization>? sort = null;

            if (request.Filter is not null)
            {
                if (!string.IsNullOrWhiteSpace(request.Filter.Search))
                {
                    var regex = new BsonRegularExpression(Regex.Escape(request.Filter.Search.Trim()), "i");
                    filter &= Builders<Organization>.Filter.Regex(x => x.Name, regex)
                        | Builders<Organization>.Filter.Regex(x => x.ShortCode, regex)
                        | Builders<Organization>.Filter.Regex(x => x.Description, regex);
                }

                if (request.Filter.Ids is { Count: > 0 })
                {
                    filter &= Builders<Organization>.Filter.In(x => x.ItemId, request.Filter.Ids);
                }

                if (request.Filter.IsDisabled.HasValue)
                {
                    filter &= Builders<Organization>.Filter.Eq(x => x.IsDisabled, request.Filter.IsDisabled.Value);
                }

                if (!string.IsNullOrWhiteSpace(request.Filter.ParentOrganizationId))
                {
                    filter &= Builders<Organization>.Filter.Eq(x => x.ParentOrganizationId, request.Filter.ParentOrganizationId);
                }
            }

            if (request.Sort is not null)
            {
                sort = request.Sort.IsDescending
                    ? Builders<Organization>.Sort.Descending(request.Sort.Property)
                    : Builders<Organization>.Sort.Ascending(request.Sort.Property);
            }
            else
            {
                sort = Builders<Organization>.Sort.Ascending(x => x.Name);
            }

            var totalCount = await collection.CountDocumentsAsync(filter);

            var organizations = await collection.Find(filter)
                .Sort(sort)
                .Skip(request.PageSize * request.Page)
                .Limit(request.PageSize)
                .ToListAsync();

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
                    .Set(c=> c.DefaultPermissionsForNewUserOnSignUp, config.DefaultPermissionsForNewUserOnSignUp)
                    .Set(c => c.LastUpdatedDate, config.LastUpdatedDate), new UpdateOptions { IsUpsert = true });
        }

        private static string ResolveOrganizationId(string? organizationId)
        {
            organizationId = !string.IsNullOrWhiteSpace(organizationId)? organizationId:  BlocksContext.GetContext()?.OrganizationId;
            return string.IsNullOrWhiteSpace(organizationId) ? "default" : organizationId;
        }


        private async Task<long> CountRoleUsageAcrossOrganizationAsync(string slug, string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");
            var filter = Builders<Permission>.Filter.AnyEq(r => r.Roles, slug) &
                         Builders<Permission>.Filter.Eq(r => r.OrganizationId, organizationId);

            return await collection.CountDocumentsAsync(filter);
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

        public async Task<List<Permission>> GetFeResourceFeaturesAsync(List<string> roleSlugs, List<string> permissionKeys, string? search = null, bool? isBuiltIn = null, string? organizationId = null)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<Permission>("Permissions");
            var resolvedOrgId = ResolveOrganizationId(organizationId);

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

var filter = Builders<Permission>.Filter.Eq(x => x.OrganizationId, resolvedOrgId)
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
