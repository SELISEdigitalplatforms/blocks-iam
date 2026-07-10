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

        private IMongoCollection<IdentityEvent> IdentityEventsCollection() =>
            GetDatabase().GetCollection<IdentityEvent>("IdentityEvents");

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
            var match = new BsonDocument
            {
                { "UserId", userId }
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                match.Add("TenantId", tenantId);
            }
            if (req.Filter?.ActiveOnly == true)
            {
                match.Add("IsActive", true);
            }
            if (!string.IsNullOrWhiteSpace(req.Filter?.ClientId))
            {
                match.Add("ClientId", req.Filter.ClientId);
            }

            var skip = Math.Max(req.Page, 0) * Math.Max(req.PageSize, 1);
            var limit = Math.Max(req.PageSize, 1);

            var pipeline = new BsonDocument[]
            {
                new BsonDocument("$match", match),
                new BsonDocument("$sort", new BsonDocument("UpdatedAt", -1)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$SessionId", BsonNull.Value }),
                            new BsonDocument("$concat", new BsonArray
                            {
                                "null:",
                                "$UserId",
                                ":",
                                "$TenantId",
                                ":",
                                new BsonDocument("$ifNull", new BsonArray { "$ClientId", string.Empty }),
                                ":",
                                new BsonDocument("$ifNull", new BsonArray { "$DeviceInformation.Device", string.Empty })
                            }),
                            "$SessionId"
                        })
                    },
                    { "doc", new BsonDocument("$first", "$$ROOT") }
                }),
                new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc")),
                new BsonDocument("$sort", new BsonDocument("UpdatedAt", -1)),
                new BsonDocument("$skip", skip),
                new BsonDocument("$limit", limit)
            };

            var rows = await IdentitySessions()
                .Aggregate<IdentitySession>(pipeline, cancellationToken: ct)
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

        public async Task<IReadOnlyList<RefreshTokenRotationDto>> GetRotationHistoryAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            var b = Builders<RefreshTokenModel>.Filter;
            var filter = b.Eq(x => x.SessionId, sessionId);
            var rows = await RefreshTokens().Find(filter).SortBy(x => x.AbsoluteExpiry).ToListAsync(ct);
            return rows.Select(r => new RefreshTokenRotationDto
            {
                TokenId = r.TokenId,
                IssuedAt = r.SlidingExpiry,
                AbsoluteExpiry = r.AbsoluteExpiry,
                IsRevoked = r.IsRevoked,
                RevokedAt = r.RevokedAt,
                RevokeReason = r.RevokeReason,
                IpAddress = r.IpAddress,
                UserAgent = r.UserAgent
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

        public async Task<IReadOnlyList<AuthHistoryDto>> GetHistoryAsync(string userId, GetHistoryRequest req, CancellationToken ct)
        {
            // History is the union of UserAuthenticationTimelines (logout / backchannel / admin revoke /
            // password grant timeline) and IdentityEvents (refresh-token / rotation / token-endpoint events).
            // Pagination (skip/limit) applies after the union. Some events (e.g. password grant) appear
            // in both collections because they record distinct phases (login decision vs token issue);
            // duplication is by design and the UI can distinguish them by Event name.
            var skip = Math.Max(req.Page, 0) * Math.Max(req.PageSize, 1);
            var limit = Math.Max(req.PageSize, 1);

            var timelineTask = QueryTimelinesAsync(userId, req, ct);
            var identityEventTask = QueryIdentityEventsAsync(userId, req, ct);

            await Task.WhenAll(timelineTask, identityEventTask);

            var merged = timelineTask.Result
                .Concat(identityEventTask.Result)
                .OrderByDescending(x => x.CreatedDate)
                .Skip(skip)
                .Take(limit)
                .ToList();

            return merged;
        }

        private async Task<List<AuthHistoryDto>> QueryTimelinesAsync(string userId, GetHistoryRequest req, CancellationToken ct)
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
                if (!string.IsNullOrWhiteSpace(req.Filter.Outcome))
                {
                    filters.Add(b.Eq(x => x.Outcome, req.Filter.Outcome));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.ReasonCode))
                {
                    filters.Add(b.Eq(x => x.ReasonCode, req.Filter.ReasonCode));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.ClientId))
                {
                    filters.Add(b.Eq(x => x.ClientId, req.Filter.ClientId));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.TenantId))
                {
                    filters.Add(b.Eq(x => x.TenantId, req.Filter.TenantId));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.SessionId))
                {
                    filters.Add(b.Eq(x => x.SessionId, req.Filter.SessionId));
                }
            }

            var rows = await Timelines()
                .Find(b.And(filters))
                .SortByDescending(x => x.CreatedDate)
                .ToListAsync(ct);

            return rows.Select(MapHistory).ToList();
        }

        private async Task<List<AuthHistoryDto>> QueryIdentityEventsAsync(string userId, GetHistoryRequest req, CancellationToken ct)
        {
            var b = Builders<IdentityEvent>.Filter;
            var filters = new List<FilterDefinition<IdentityEvent>>
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
                    filters.Add(b.Gte(x => x.CreatedAt, req.Filter.From.Value));
                }
                if (req.Filter.To.HasValue)
                {
                    filters.Add(b.Lte(x => x.CreatedAt, req.Filter.To.Value));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.IpAddress))
                {
                    filters.Add(b.Eq(x => x.IpAddresses, req.Filter.IpAddress));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.Outcome))
                {
                    filters.Add(b.Eq(x => x.Outcome, req.Filter.Outcome));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.ReasonCode))
                {
                    filters.Add(b.Eq(x => x.ReasonCode, req.Filter.ReasonCode));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.ClientId))
                {
                    filters.Add(b.Eq(x => x.ClientId, req.Filter.ClientId));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.TenantId))
                {
                    filters.Add(b.Eq(x => x.TenantId, req.Filter.TenantId));
                }
                if (!string.IsNullOrWhiteSpace(req.Filter.SessionId))
                {
                    filters.Add(b.Eq(x => x.SessionId, req.Filter.SessionId));
                }
            }

            var rows = await IdentityEventsCollection()
                .Find(b.And(filters))
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

            return rows.Select(MapIdentityEvent).ToList();
        }

        public async Task<IReadOnlyList<AuthHistoryDto>> GetSessionLifecycleAsync(string userId, string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            var b = Builders<UserAuthenticationTimeline>.Filter;
            var timelineFilters = new List<FilterDefinition<UserAuthenticationTimeline>>
            {
                b.Eq(x => x.UserId, userId),
                b.Eq(x => x.SessionId, sessionId)
            };
            var timelineTask = Timelines()
                .Find(b.And(timelineFilters))
                .SortByDescending(x => x.CreatedDate)
                .Limit(200)
                .ToListAsync(ct);

            var eb = Builders<IdentityEvent>.Filter;
            var eventFilters = new List<FilterDefinition<IdentityEvent>>
            {
                eb.Eq(x => x.UserId, userId),
                eb.Eq(x => x.SessionId, sessionId)
            };
            var eventTask = IdentityEventsCollection()
                .Find(eb.And(eventFilters))
                .SortByDescending(x => x.CreatedAt)
                .Limit(200)
                .ToListAsync(ct);

            await Task.WhenAll(timelineTask, eventTask);

            return timelineTask.Result
                .Select(MapHistory)
                .Concat(eventTask.Result.Select(MapIdentityEvent))
                .OrderByDescending(x => x.CreatedDate)
                .Take(200)
                .ToList();
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
                    new CreateIndexOptions { Name = "ix_user_session" }),
                new CreateIndexModel<IdentitySession>(
                    Builders<IdentitySession>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.SessionId),
                    new CreateIndexOptions { Name = "ix_user_tenant_session" })
            }, ct);

            await Timelines().Indexes.CreateOneAsync(
                new CreateIndexModel<UserAuthenticationTimeline>(
                    Builders<UserAuthenticationTimeline>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Descending(x => x.CreatedDate),
                    new CreateIndexOptions { Name = "ix_user_created" }),
                cancellationToken: ct);

            await IdentityEventsCollection().Indexes.CreateOneAsync(
                new CreateIndexModel<IdentityEvent>(
                    Builders<IdentityEvent>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Descending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "ix_user_created" }),
                cancellationToken: ct);

            await RefreshTokens().Indexes.CreateManyAsync(new[]
            {
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
            SessionId = r.SessionId,
            TenantId = r.TenantId,
            ClientId = r.ClientId,
            CorrelationId = r.CorrelationId,
            Outcome = r.Outcome,
            ReasonCode = r.ReasonCode,
            RiskLevel = r.RiskLevel,
            CreatedDate = r.CreatedDate
        };

        private static AuthHistoryDto MapIdentityEvent(IdentityEvent r) => new()
        {
            Event = r.Event,
            ActionBy = r.ActionBy,
            DeviceInformation = r.DeviceInformation,
            IpAddresses = r.IpAddresses,
            SessionId = r.SessionId,
            TenantId = r.TenantId,
            ClientId = r.ClientId,
            CorrelationId = r.CorrelationId,
            Outcome = r.Outcome,
            ReasonCode = r.ReasonCode,
            RiskLevel = r.RiskLevel,
            CreatedDate = r.CreatedAt
        };
    }
}