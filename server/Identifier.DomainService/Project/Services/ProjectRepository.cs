using Blocks.Genesis;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Entities;
using Identifier.DomainService.Shared;
using Identifier.DomainService.Shared.Entities;
using Identifier.DomainService.Shared.Services;
using MongoDB.Driver;

namespace Identifier.DomainService.Projects
{
    public class ProjectRepository : IProjectRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public ProjectRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<Tenant> GetByIdAsync(string itemId)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.ItemId, itemId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Tenant> GetByDomainAsync(string name)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.ApplicationDomain, name);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<(TenantAsset? assets, long totalCount)> GetTenantAssetAsync(GetAssetRequest request)
        {
            var sharedProjects = await GetProjectPeoplesAsync(request.TenantGroupId);
            if (sharedProjects == null || sharedProjects.Count == 0)
            {
                return (null, 0);
            }

            var collection = _dbContextProvider.GetCollection<TenantAsset>(IdentifierConstants.TenantAssetCollectionName);
            var documentFilter = Builders<TenantAsset>.Filter.Eq(mc => mc.TenantGroupId, request.TenantGroupId);
            var tenantAsset = await collection.Find(documentFilter).FirstOrDefaultAsync();

            if (tenantAsset == null)
                return (null, 0);

            var filteredResources = tenantAsset.Resources?.AsEnumerable() ?? Enumerable.Empty<Resource>();

            if (request.Filter != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Filter.Name))
                {
                    filteredResources = filteredResources.Where(r =>
                        r.Name != null && r.Name.Contains(request.Filter.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(request.Filter.Link))
                {
                    filteredResources = filteredResources.Where(r =>
                        r.Link != null && r.Link.Contains(request.Filter.Link.Trim(), StringComparison.OrdinalIgnoreCase));
                }
            }

            var totalCount = filteredResources.Count();

            var pagedResources = filteredResources
                .Skip(request.PageSize * request.Page)
                .Take(request.PageSize)
                .ToList();

            tenantAsset.Resources = pagedResources;
            return (tenantAsset, totalCount);
        }

        public async Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request)
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);

            var filter = !string.IsNullOrEmpty(request.TenantGroupId) ?

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false),
                                                       Builders<Project>.Filter.Eq(mc => mc.TenantGroupId, request.TenantGroupId)) :

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));

            var option = new FindOptions<Project>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            };

            using var cursor = await collection.FindAsync(filter, option);
            var selfProjects = await cursor.ToListAsync();
            var selfGroupedProjectsTasks = selfProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = false,
                                                   NonSharedProject = []
                                               }).ToList();


            var sharedProjects = await GetSharedProjectsAsync(request.TenantGroupId);

            var sharedGroupProjects = sharedProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(async g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = true,
                                                   NonSharedProject = await GetNosharedProjectsAsync(sharedProjects, g.Key)
                                               }).ToList();

            var groupedSharedProject = (await Task.WhenAll(sharedGroupProjects)).ToList();
            return [.. selfGroupedProjectsTasks, .. groupedSharedProject];
        }

        private async Task<List<Project>> GetNosharedProjectsAsync(List<Project> sharedProjects, string tenantGroupId)
        {
            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.Nin(p => p.TenantId, sharedProjects?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Where(p => p.TenantGroupId == tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<Project>> GetSharedProjectsAsync(string? tenantGroupId = null)
        {
            var projectPeopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Ne(p => p.CreatedBy, BlocksContext.GetContext().UserId);

            if (!string.IsNullOrEmpty(tenantGroupId))
            {
                filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);
            }

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId)
        {
            var projectPeopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false);

            filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<ProjectStatusTracer>> GetAllUnfinishedProjectAsync()
        {
            var collection = _dbContextProvider.GetCollection<ProjectStatusTracer>(IdentifierConstants.ProjectStatusTracerCollectionName);

            var filter = Builders<ProjectStatusTracer>.Filter.Eq(mc => mc.IsProjectCreationSuccess, false);
            var unfinishedList = await collection.FindAsync(filter);
            return await unfinishedList.ToListAsync();
        }

        public async Task<long> GetProjectCountAsync()
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                      Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));

            return await collection.CountDocumentsAsync(filter);
        }

        public async Task<bool> IsExistingEnviroment(List<string> enviroments, string tenantGroupId)
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.And(Builders<Project>.Filter.In(mc => mc.Environment, enviroments),
                                                      Builders<Project>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId),
                                                      Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));
            var count = await collection.CountDocumentsAsync(filter);
            return count > 0;
        }

        public async Task<Tenant> GetByTenantIdAsync(string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.TenantId, tenantId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<List<SsoInfo>> GetSsoInfoAsync()
        {
            var collection = _dbContextProvider.GetCollection<SsoInfo>("SocialLoginCredentials");

            var filter = Builders<SsoInfo>.Filter.Eq(mc => mc.IsDisabled, false);
            return await (await collection.FindAsync(filter)).ToListAsync();
        }

        public async Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId)
        {
            var collection = _dbContextProvider.GetCollection<BlocksGuid>($"{nameof(BlocksGuid)}s");
            var filter = Builders<BlocksGuid>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<ThirdPartyJWTClaims> GetThirdPartyJWTClaimsAsync(string itemId)
        {
            var collection = _dbContextProvider.GetCollection<ThirdPartyJWTClaims>("ThirdPartyJWTClaims");

            var filter = !string.IsNullOrWhiteSpace(itemId) ?
                         Builders<ThirdPartyJWTClaims>.Filter.Eq(mc => mc.ItemId, itemId) :
                         Builders<ThirdPartyJWTClaims>.Filter.Empty;

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetProjectIdsByGroupId(string projectGroupId)
        {
            var filter = Builders<Tenant>.Filter.Eq(x => x.TenantGroupId, projectGroupId);

            var tenantIds = await _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)
                .Find(filter)
                .Project(x => x.TenantId)
                .ToListAsync();

            return tenantIds;
        }


    }
}
