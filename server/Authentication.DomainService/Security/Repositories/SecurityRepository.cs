using Authentication.DomainService.Entities;
using Authentication.DomainService.Security.Models;
using Blocks.Genesis;
using Iam.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Security.Repositories
{
    public sealed class SecurityRepository : ISecurityRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private readonly IIamDataAccess _dataAccess;

        public SecurityRepository(IDbContextProvider dbContextProvider, IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        private IMongoCollection<RefreshTokenModel> RefreshTokens() =>
            GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");

        private IMongoCollection<IdpSessionModel> IdpSessions() =>
            GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");

        private IMongoCollection<ImpersonationSession> ImpersonationSessions() =>
            GetDatabase().GetCollection<ImpersonationSession>("ImpersonationSessions");

        private static bool IsTokenActive(RefreshTokenModel r, DateTime now) =>
            !r.IsRevoked && now < r.AbsoluteExpiry;

        public async Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(
            string userId,
            string? clientId,
            bool activeOnly,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var now = DateTime.UtcNow;

            var match = new BsonDocument
            {
                { "UserId", userId },
                { "IsRevoked", false },
                { "AbsoluteExpiry", new BsonDocument("$gt", now) }
            };
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                match.Add("ClientId", clientId);
            }

            // Aggregation: the resolved READ filter becomes the first $match; the resolver projection is not applied.
            var matchScope = await _dataAccess.ReadAsync(
                IamSchemas.RefreshTokenModel,
                new BsonDocumentFilterDefinition<RefreshTokenModel>(match),
                "SecurityRepository.GetUserSessionsAsync",
                useProjection: false);
            if (!matchScope.Granted)
            {
                return [];
            }

            match = matchScope.Filter.Render(new RenderArgs<RefreshTokenModel>(
                BsonSerializer.LookupSerializer<RefreshTokenModel>(), BsonSerializer.SerializerRegistry));

            var groupPipeline = new BsonDocument[]
            {
                new BsonDocument("$match", match),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument
                        {
                            { "SessionId", "$SessionId" },
                            { "ClientId", "$ClientId" }
                        }
                    },
                    { "doc", new BsonDocument("$first", "$$ROOT") },
                    { "appCount", new BsonDocument("$sum", 1) }
                }),
                new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc"))
            };

            var tokenRows = await RefreshTokens()
                .Aggregate<RefreshTokenModel>(groupPipeline, cancellationToken: ct)
                .ToListAsync(ct);

            var sessionIds = tokenRows
                .Select(r => r.SessionId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .Distinct()
                .ToList();

            var idpSessionLookup = new Dictionary<string, IdpSessionModel>(StringComparer.Ordinal);
            if (sessionIds.Count > 0)
            {
                var idpSessionScope = await _dataAccess.ReadAsync(
                    IamSchemas.IdpSessionModel,
                    Builders<IdpSessionModel>.Filter.In(s => s.SessionId, sessionIds),
                    "SecurityRepository.GetUserSessionsAsync.IdpSessions",
                    useProjection: false);
                var idpSessions = idpSessionScope.Granted
                    ? await idpSessionScope.Apply(IdpSessions()).ToListAsync(ct)
                    : [];
                foreach (var s in idpSessions)
                {
                    idpSessionLookup[s.SessionId] = s;
                }
            }

            var groups = tokenRows
                .Where(r => !string.IsNullOrWhiteSpace(r.SessionId))
                .GroupBy(r => r.SessionId!, StringComparer.Ordinal)
                .Select(g =>
                {
                    var apps = g.ToList();
                    var firstApp = apps.First();
                    idpSessionLookup.TryGetValue(g.Key, out var idpSession);
                    var primary = apps.FirstOrDefault(a => IsTokenActive(a, now)) ?? firstApp;
                    return new UserSessionDto
                    {
                        SessionId = g.Key,
                        TenantId = firstApp.TenantId ?? string.Empty,
                        UserId = firstApp.UserId,
                        CreatedAt = idpSession?.CreatedAt ?? firstApp.IssuedUtc,
                        LastActivityAt = idpSession?.LastActivityAt ?? firstApp.IssuedUtc,
                        AbsoluteExpiry = firstApp.AbsoluteExpiry,
                        IdleExpiry = firstApp.SlidingExpiry,
                        Status = SessionStatus.Active,
                        PrimaryDeviceName = primary.DeviceInformation?.Device ?? firstApp.DeviceInformation?.Device,
                        PrimaryOperatingSystem = primary.DeviceInformation?.OS ?? firstApp.DeviceInformation?.OS,
                        PrimaryBrowser = primary.DeviceInformation?.Browser ?? firstApp.DeviceInformation?.Browser,
                        PrimaryIpAddress = primary.IpAddress ?? firstApp.IpAddress,
                        ApplicationCount = apps.Count,
                        ClientIds = apps
                            .Select(a => a.ClientId)
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Cast<string>()
                            .Distinct(StringComparer.Ordinal)
                            .ToList()
                    };
                })
                .Where(g => !activeOnly || g.Status == SessionStatus.Active)
                .OrderByDescending(g => g.LastActivityAt)
                .ToList();

            return groups;
        }

        public async Task<UserSessionDto?> GetUserSessionAsync(
            string userId,
            string sessionId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var b = Builders<RefreshTokenModel>.Filter;
            var filters = new List<FilterDefinition<RefreshTokenModel>>
            {
                b.Eq(x => x.UserId, userId),
                b.Eq(x => x.SessionId, sessionId)
            };

            var rowScope = await _dataAccess.ReadAsync(IamSchemas.RefreshTokenModel, b.And(filters), "SecurityRepository.GetUserSessionAsync", useProjection: false);
            if (!rowScope.Granted)
            {
                return null;
            }

            var rows = await rowScope.Apply(RefreshTokens()).ToListAsync(ct);
            if (rows.Count == 0)
            {
                return null;
            }

            var idpSessionOneScope = await _dataAccess.ReadAsync(
                IamSchemas.IdpSessionModel,
                Builders<IdpSessionModel>.Filter.Eq(x => x.SessionId, sessionId),
                "SecurityRepository.GetUserSessionAsync.IdpSession",
                useProjection: false);
            var idpSession = idpSessionOneScope.Granted
                ? await idpSessionOneScope.Apply(IdpSessions()).FirstOrDefaultAsync(ct)
                : null;

            var now = DateTime.UtcNow;
            var firstApp = rows.First();
            var primary = rows.FirstOrDefault(a => IsTokenActive(a, now)) ?? firstApp;
            return new UserSessionDto
            {
                SessionId = sessionId,
                TenantId = firstApp.TenantId ?? string.Empty,
                UserId = firstApp.UserId,
                CreatedAt = idpSession?.CreatedAt ?? firstApp.IssuedUtc,
                LastActivityAt = idpSession?.LastActivityAt ?? firstApp.IssuedUtc,
                AbsoluteExpiry = firstApp.AbsoluteExpiry,
                IdleExpiry = firstApp.SlidingExpiry,
                Status = rows.Any(a => IsTokenActive(a, now)) ? SessionStatus.Active : SessionStatus.Expired,
                PrimaryDeviceName = primary.DeviceInformation?.Device ?? firstApp.DeviceInformation?.Device,
                PrimaryOperatingSystem = primary.DeviceInformation?.OS ?? firstApp.DeviceInformation?.OS,
                PrimaryBrowser = primary.DeviceInformation?.Browser ?? firstApp.DeviceInformation?.Browser,
                PrimaryIpAddress = primary.IpAddress ?? firstApp.IpAddress,
                ApplicationCount = rows.Count,
                ClientIds = rows
                    .Select(a => a.ClientId)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }

        public async Task<IReadOnlyList<SessionRotationRecord>> GetRotationHistoryAsync(
            string sessionId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            var b = Builders<RefreshTokenModel>.Filter;
            var filter = b.Eq(x => x.SessionId, sessionId);
            var historyScope = await _dataAccess.ReadAsync(IamSchemas.RefreshTokenModel, filter, "SecurityRepository.GetRotationHistoryAsync", useProjection: false);
            if (!historyScope.Granted)
            {
                return [];
            }

            var rows = await historyScope.Apply(RefreshTokens()).SortBy(x => x.AbsoluteExpiry).ToListAsync(ct);
            var count = rows.Count;
            return rows.Select((r, idx) => new SessionRotationRecord
            {
                ClientId = r.ClientId,
                IssuedUtc = r.IssuedUtc,
                AbsoluteExpiry = r.AbsoluteExpiry,
                IsRevoked = r.IsRevoked,
                RevokedAt = r.RevokedAt,
                RevokeReason = r.RevokeReason,
                IsCurrent = idx == count - 1 && !r.IsRevoked
            }).ToList();
        }

        public async Task EnsureIndexesAsync(CancellationToken ct)
        {
            await RefreshTokens().Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.SessionId)
                        .Ascending(x => x.ClientId)
                        .Ascending(x => x.IsRevoked),
                    new CreateIndexOptions { Name = "ix_session_client_revoked" }),
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.IsRevoked)
                        .Ascending(x => x.AbsoluteExpiry),
                    new CreateIndexOptions { Name = "ix_user_tenant_active_expiry" }),
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.SessionId)
                        .Ascending(x => x.IsRevoked),
                    new CreateIndexOptions { Name = "ix_session_revoked" }),
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.SessionId)
                        .Descending(x => x.AbsoluteExpiry),
                    new CreateIndexOptions { Name = "ix_session_absolute_expiry" }),
                // Family revocation filters on exactly these two fields.
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.RefreshTokenSessionId)
                        .Ascending(x => x.IsRevoked),
                    new CreateIndexOptions { Name = "ix_refresh_token_session_id" })
            }, ct);

            await ImpersonationSessions().Indexes.CreateOneAsync(
                new CreateIndexModel<ImpersonationSession>(
                    Builders<ImpersonationSession>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.Status),
                    new CreateIndexOptions { Name = "ix_user_status" }),
                cancellationToken: ct);
        }
    }
}
