using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Authentication.DomainService.Security.Repositories
{
    public sealed class SecurityRepository : ISecurityRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public SecurityRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        private IMongoCollection<RefreshTokenModel> RefreshTokens() =>
            GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");

        private IMongoCollection<TokenRevocationModel> RevokedTokens() =>
            GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");

        private IMongoCollection<IdpSessionModel> IdpSessions() =>
            GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");

        private IMongoCollection<ImpersonationSession> ImpersonationSessions() =>
            GetDatabase().GetCollection<ImpersonationSession>("ImpersonationSessions");

        public async Task<IReadOnlyList<SessionGroupDto>> GetSessionGroupsAsync(
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

            // Aggregate active refresh tokens into per-(SessionId, ClientId) groups, then join IdpSessions
            // for createdAt / lastActivityAt.
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
                var idpSessions = await IdpSessions()
                    .Find(Builders<IdpSessionModel>.Filter.In(s => s.SessionId, sessionIds))
                    .ToListAsync(ct);
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
                    var apps = g.Select(MapActiveSession).ToList();
                    var firstApp = g.First();
                    idpSessionLookup.TryGetValue(g.Key, out var idpSession);
                    return new SessionGroupDto
                    {
                        SessionId = g.Key,
                        TenantId = firstApp.TenantId ?? string.Empty,
                        UserId = firstApp.UserId,
                        CreatedAt = idpSession?.CreatedAt ?? firstApp.IssuedUtc,
                        LastActivityAt = idpSession?.LastActivityAt ?? firstApp.IssuedUtc,
                        Apps = apps
                    };
                })
                .Where(g => !activeOnly || g.Apps.Any(a => a.IsActive))
                .ToList();

            return groups;
        }

        public async Task<SessionGroupDto?> GetSessionGroupAsync(
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

            var rows = await RefreshTokens().Find(b.And(filters)).ToListAsync(ct);
            if (rows.Count == 0)
            {
                return null;
            }

            var idpSession = await IdpSessions()
                .Find(Builders<IdpSessionModel>.Filter.Eq(x => x.SessionId, sessionId))
                .FirstOrDefaultAsync(ct);

            var firstApp = rows.First();
            return new SessionGroupDto
            {
                SessionId = sessionId,
                TenantId = firstApp.TenantId ?? string.Empty,
                UserId = firstApp.UserId,
                CreatedAt = idpSession?.CreatedAt ?? firstApp.IssuedUtc,
                LastActivityAt = idpSession?.LastActivityAt ?? firstApp.IssuedUtc,
                Apps = rows.Select(MapActiveSession).ToList()
            };
        }

        public async Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string tokenId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return null;
            }

            var row = await RefreshTokens()
                .Find(Builders<RefreshTokenModel>.Filter.Eq(x => x.TokenId, tokenId))
                .FirstOrDefaultAsync(ct);
            if (row == null)
            {
                return null;
            }

            return new RefreshTokenStatus
            {
                TokenId = row.TokenId,
                IsRevoked = row.IsRevoked,
                IssuedAt = row.IssuedUtc,
                AbsoluteExpiry = row.AbsoluteExpiry,
                RevokedAt = row.RevokedAt,
                RevokeReason = row.RevokeReason
            };
        }

        public async Task<IReadOnlyList<RefreshTokenRotationDto>> GetRotationHistoryAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            var b = Builders<RefreshTokenModel>.Filter;
            var filter = b.Eq(x => x.SessionId, sessionId);
            var rows = await RefreshTokens().Find(filter).SortBy(x => x.AbsoluteExpiry).ToListAsync(ct);
            var count = rows.Count;
            return rows.Select((r, idx) => new RefreshTokenRotationDto
            {
                Fingerprint = r.TokenId.Length >= 6 ? r.TokenId.Substring(0, 6) : r.TokenId,
                ClientId = r.ClientId,
                OrganizationId = r.OrganizationId,
                GrantType = r.GrantType,
                IssuedUtc = r.IssuedUtc,
                AbsoluteExpiry = r.AbsoluteExpiry,
                IsRevoked = r.IsRevoked,
                RevokedAt = r.RevokedAt,
                RevokeReason = r.RevokeReason,
                IpAddress = r.IpAddress,
                UserAgent = r.UserAgent,
                IsCurrent = idx == count - 1 && !r.IsRevoked
            }).ToList();
        }

        public async Task<IReadOnlyList<RevokedAccessTokenDto>> GetRevokedAccessTokensAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var filter = Builders<TokenRevocationModel>.Filter.Eq(x => x.UserId, userId);
            var rows = await RevokedTokens()
                .Find(filter)
                .SortByDescending(x => x.RevokedAt)
                .Limit(500)
                .ToListAsync(ct);

            return rows.Select(r => new RevokedAccessTokenDto
            {
                Jti = r.Jti,
                RevokedAt = r.RevokedAt,
                Reason = r.RevokeReason
            }).ToList();
        }

        public async Task<IdpSessionSummaryDto?> GetIdpSessionAsync(string userId, string? tenantId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var b = Builders<IdpSessionModel>.Filter;
            var filters = new List<FilterDefinition<IdpSessionModel>>
            {
                b.ElemMatch(x => x.Accounts, a => a.UserId == userId)
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                filters = new List<FilterDefinition<IdpSessionModel>>
                {
                    b.Eq(x => x.TenantId, tenantId),
                    b.ElemMatch(x => x.Accounts, a => a.UserId == userId && a.TenantId == tenantId)
                };
            }

            var row = await IdpSessions()
                .Find(b.And(filters))
                .SortByDescending(x => x.LastActivityAt)
                .FirstOrDefaultAsync(ct);

            if (row == null)
            {
                return null;
            }

            return new IdpSessionSummaryDto
            {
                SessionId = row.SessionId,
                TenantId = row.TenantId,
                Accounts = row.Accounts.Select(a => new IdpSessionAccountDto
                {
                    UserId = a.UserId,
                    TenantId = a.TenantId,
                    DisplayName = a.DisplayName,
                    LoginAt = a.LoginAt
                }).ToList(),
                IpAddress = row.IpAddress,
                CreatedAt = row.CreatedAt,
                LastActivityAt = row.LastActivityAt,
                IdleExpiry = row.IdleExpiry,
                AbsoluteExpiry = row.AbsoluteExpiry,
                IsRevoked = row.RevokedAt.HasValue || row.IsExpired()
            };
        }

        public async Task<IReadOnlyList<ImpersonationSummaryDto>> GetImpersonationsAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var b = Builders<ImpersonationSession>.Filter;
            var filter = b.And(
                b.Eq(x => x.UserId, userId),
                b.Eq(x => x.Status, "active")
            );

            var rows = await ImpersonationSessions()
                .Find(filter)
                .SortByDescending(x => x.StartedAt)
                .ToListAsync(ct);

            return rows.Select(r => new ImpersonationSummaryDto
            {
                Id = r.Id,
                StartedAt = r.StartedAt,
                EndedAt = r.EndedAt,
                RootTenantId = r.RootTenantId,
                TargetTenantId = r.TargetTenantId,
                Status = r.Status,
                Reason = r.Reason
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
                    new CreateIndexOptions { Name = "ix_session_absolute_expiry" })
            }, ct);

            await RevokedTokens().Indexes.CreateOneAsync(
                new CreateIndexModel<TokenRevocationModel>(
                    Builders<TokenRevocationModel>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { Name = "ix_user_expires" }),
                cancellationToken: ct);

            await ImpersonationSessions().Indexes.CreateOneAsync(
                new CreateIndexModel<ImpersonationSession>(
                    Builders<ImpersonationSession>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.Status),
                    new CreateIndexOptions { Name = "ix_user_status" }),
                cancellationToken: ct);
        }

        private static ActiveSessionDto MapActiveSession(RefreshTokenModel r)
        {
            var now = DateTime.UtcNow;
            return new ActiveSessionDto
            {
                TokenId = r.TokenId,
                SessionId = r.SessionId ?? string.Empty,
                UserId = r.UserId ?? string.Empty,
                TenantId = r.TenantId ?? string.Empty,
                OrganizationId = r.OrganizationId,
                ClientId = r.ClientId,
                GrantType = r.GrantType,
                IpAddresses = r.IpAddress,
                UserAgent = r.UserAgent,
                DeviceName = r.DeviceInformation?.Device,
                DeviceModel = r.DeviceInformation?.Model,
                OperatingSystem = r.DeviceInformation?.OS,
                Browser = r.DeviceInformation?.Browser,
                IssuedUtc = r.IssuedUtc,
                SlidingExpiry = r.SlidingExpiry,
                AbsoluteExpiry = r.AbsoluteExpiry,
                IsActive = !r.IsRevoked && now < r.AbsoluteExpiry,
                Impersonated = r.Impersonated,
                ImpersonationId = r.ImpersonationId
            };
        }
    }
}
