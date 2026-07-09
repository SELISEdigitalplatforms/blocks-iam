using System.Text.RegularExpressions;
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

        private IMongoCollection<IdentitySession> IdentitySessions() =>
            GetDatabase().GetCollection<IdentitySession>("IdentitySessions");

        private IMongoCollection<UserAuthenticationTimeline> Timelines() =>
            GetDatabase().GetCollection<UserAuthenticationTimeline>("UserAuthenticationTimelines");

        private IMongoCollection<RefreshTokenModel> RefreshTokens() =>
            GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");

        private IMongoCollection<TokenRevocationModel> RevokedTokens() =>
            GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");

        private IMongoCollection<IdpSessionModel> IdpSessions() =>
            GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");

        private IMongoCollection<ImpersonationSession> ImpersonationSessions() =>
            GetDatabase().GetCollection<ImpersonationSession>("ImpersonationSessions");

        public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(string userId, string? tenantId, GetSessionsRequest req, CancellationToken ct)
        {
            var b = Builders<IdentitySession>.Filter;
            var filters = new List<FilterDefinition<IdentitySession>>
            {
                b.Eq(x => x.UserId, userId)
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                filters.Add(b.Eq(x => x.TenantId, tenantId));
            }
            if (req.Filter?.ActiveOnly == true)
            {
                filters.Add(b.Eq(x => x.IsActive, true));
            }
            if (!string.IsNullOrWhiteSpace(req.Filter?.ClientId))
            {
                filters.Add(b.Eq(x => x.ClientId, req.Filter.ClientId));
            }

            var filter = b.And(filters);
            var sort = Builders<IdentitySession>.Sort.Descending(x => x.CreatedAt);
            var skip = Math.Max(req.Page, 0) * Math.Max(req.PageSize, 1);
            var limit = Math.Max(req.PageSize, 1);

            var rows = await IdentitySessions()
                .Find(filter)
                .Sort(sort)
                .Skip(skip)
                .Limit(limit)
                .ToListAsync(ct);

            return rows.Select(MapSession).ToList();
        }

        public async Task<SessionDto?> GetSessionAsync(string userId, string? tenantId, string sessionId, CancellationToken ct)
        {
            var b = Builders<IdentitySession>.Filter;
            var filters = new List<FilterDefinition<IdentitySession>>
            {
                b.Eq(x => x.UserId, userId),
                b.Eq(x => x.SessionId, sessionId)
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                filters.Add(b.Eq(x => x.TenantId, tenantId));
            }

            var session = await IdentitySessions().Find(b.And(filters)).FirstOrDefaultAsync(ct);
            return session == null ? null : MapSession(session);
        }

        public async Task<SessionDto?> GetSessionByIdAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }
            var filter = Builders<IdentitySession>.Filter.Eq(x => x.SessionId, sessionId);
            var session = await IdentitySessions().Find(filter).FirstOrDefaultAsync(ct);
            return session == null ? null : MapSession(session);
        }

        public async Task<SessionDto?> GetSessionByRefreshTokenAsync(string userId, string? tenantId, string refreshToken, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            var b = Builders<IdentitySession>.Filter;
            var filters = new List<FilterDefinition<IdentitySession>>
            {
                b.Eq(x => x.UserId, userId),
                b.Eq(x => x.RefreshToken, refreshToken)
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                filters.Add(b.Eq(x => x.TenantId, tenantId));
            }

            var session = await IdentitySessions()
                .Find(b.And(filters))
                .SortByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            return session == null ? null : MapSession(session);
        }

        public async Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var b = Builders<RefreshTokenModel>.Filter;
            var filter = b.Eq(x => x.SessionId, sessionId);
            var row = await RefreshTokens().Find(filter).SortByDescending(x => x.AbsoluteExpiry).FirstOrDefaultAsync(ct);
            if (row == null)
            {
                return null;
            }

            return new RefreshTokenStatus
            {
                TokenId = row.TokenId,
                IsRevoked = row.IsRevoked,
                IssuedAt = row.SlidingExpiry,
                AbsoluteExpiry = row.AbsoluteExpiry,
                RevokedAt = row.RevokedAt,
                RevokeReason = row.RevokeReason
            };
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

        public async Task<IReadOnlyList<AuthHistoryDto>> GetHistoryAsync(string userId, GetHistoryRequest req, CancellationToken ct)
        {
            var b = Builders<UserAuthenticationTimeline>.Filter;
            var filters = new List<FilterDefinition<UserAuthenticationTimeline>>
            {
                b.Eq(x => x.UserId, userId)
            };

            if (req.Filter != null)
            {
                if (!string.IsNullOrWhiteSpace(req.Filter.EventType))
                {
                    filters.Add(b.Eq(x => x.Event, req.Filter.EventType));
                }
                if (req.Filter.From.HasValue)
                {
                    filters.Add(b.Gte(x => x.CreatedDate, req.Filter.From.Value));
                }
                if (req.Filter.To.HasValue)
                {
                    filters.Add(b.Lte(x => x.CreatedDate, req.Filter.To.Value));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.IpAddress))
                {
                    filters.Add(b.Eq(x => x.IpAddresses, req.Filter.IpAddress));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.Device))
                {
                    var rx = new BsonRegularExpression(Regex.Escape(req.Filter.Device), "i");
                    filters.Add(b.Or(
                        b.Regex(x => x.DeviceName, rx),
                        b.Regex(x => x.DeviceType, rx)
                    ));
                }
            }

            var sort = Builders<UserAuthenticationTimeline>.Sort.Descending(x => x.CreatedDate);
            var skip = Math.Max(req.Page, 0) * Math.Max(req.PageSize, 1);
            var limit = Math.Max(req.PageSize, 1);

            var rows = await Timelines()
                .Find(b.And(filters))
                .Sort(sort)
                .Skip(skip)
                .Limit(limit)
                .ToListAsync(ct);

            return rows.Select(MapHistory).ToList();
        }

        public async Task<IReadOnlyList<AuthHistoryDto>> GetSessionLifecycleAsync(string userId, string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var b = Builders<UserAuthenticationTimeline>.Filter;
            var filter = b.Eq(x => x.UserId, userId);

            var rows = await Timelines()
                .Find(filter)
                .SortByDescending(x => x.CreatedDate)
                .Limit(200)
                .ToListAsync(ct);

            return rows.Select(MapHistory).ToList();
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
            await IdentitySessions().Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<IdentitySession>(
                    Builders<IdentitySession>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.IsActive)
                        .Descending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "ix_user_active_created" }),
                new CreateIndexModel<IdentitySession>(
                    Builders<IdentitySession>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.SessionId),
                    new CreateIndexOptions { Name = "ix_user_session" })
            }, ct);

            await Timelines().Indexes.CreateOneAsync(
                new CreateIndexModel<UserAuthenticationTimeline>(
                    Builders<UserAuthenticationTimeline>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Descending(x => x.CreatedDate),
                    new CreateIndexOptions { Name = "ix_user_created" }),
                cancellationToken: ct);

            await RefreshTokens().Indexes.CreateOneAsync(
                new CreateIndexModel<RefreshTokenModel>(
                    Builders<RefreshTokenModel>.IndexKeys
                        .Ascending(x => x.SessionId)
                        .Ascending(x => x.IsRevoked),
                    new CreateIndexOptions { Name = "ix_session_revoked" }),
                cancellationToken: ct);

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

        private static SessionDto MapSession(IdentitySession s) => new()
        {
            SessionId = s.SessionId,
            UserId = s.UserId,
            TenantId = s.TenantId,
            OrganizationId = s.OrganizationId,
            ClientId = s.ClientId,
            DeviceName = s.DeviceInformation?.Device,
            DeviceType = s.DeviceInformation?.Model,
            OperatingSystem = s.DeviceInformation?.OS,
            Browser = s.DeviceInformation?.Browser,
            IpAddresses = s.IpAddresses,
            GrantType = s.GrantType,
            IssuedUtc = s.IssuedUtc,
            ExpiresUtc = s.ExpiresUtc,
            LastActivityAt = s.UpdatedAt,
            IsActive = s.IsActive
        };

        private static AuthHistoryDto MapHistory(UserAuthenticationTimeline r) => new()
        {
            Event = r.Event,
            ActionBy = r.ActionBy,
            DeviceName = r.DeviceName,
            DeviceType = r.DeviceType,
            DeviceInformation = r.DeviceInformation,
            IpAddresses = r.IpAddresses,
            CreatedDate = r.CreatedDate
        };
    }
}