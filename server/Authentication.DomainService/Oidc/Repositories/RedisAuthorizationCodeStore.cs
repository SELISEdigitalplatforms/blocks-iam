using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Authorization codes in Redis: one hash per code, key <c>iam:&lt;tenantId&gt;:authcode:&lt;sha256hex(code)&gt;</c>,
    /// TTL 660 s set together with the fields. Every change after the create is one Lua script that
    /// first checks the key exists, so no script can ever create a key without a TTL.
    /// Codes written by older pods are still read (and consumed) from MongoDB during the rollout.
    /// A Redis error is never read as "not found": it fails the request.
    /// </summary>
    public sealed class RedisAuthorizationCodeStore : IAuthorizationCodeRepository
    {
        /// <summary>One minute over the code's 10-minute lifetime, so a late exchange never loses the key mid-way.</summary>
        public static readonly TimeSpan KeyTtl = TimeSpan.FromSeconds(660);

        private const string DataField = "data";

        private const string ConsumeScript = @"
if redis.call('EXISTS', KEYS[1]) == 0 then return -1 end
return redis.call('HSETNX', KEYS[1], 'consumedAt', ARGV[1])";

        private const string MintScript = @"
if redis.call('EXISTS', KEYS[1]) == 0 then return -1 end
if redis.call('HEXISTS', KEYS[1], 'revoked') == 1 then return 0 end
redis.call('HSET', KEYS[1], 'minted', ARGV[1])
return 1";

        private const string ReuseScript = @"
if redis.call('EXISTS', KEYS[1]) == 0 then return -1 end
redis.call('HSET', KEYS[1], 'revoked', '1')
local minted = redis.call('HGET', KEYS[1], 'minted')
if minted then return minted end
return 0";

        private readonly ICacheClient _cacheClient;
        private readonly AuthorizationCodeRepository _mongoFallback;
        private readonly ILogger<RedisAuthorizationCodeStore> _logger;

        public RedisAuthorizationCodeStore(
            ICacheClient cacheClient,
            AuthorizationCodeRepository mongoFallback,
            ILogger<RedisAuthorizationCodeStore> logger)
        {
            _cacheClient = cacheClient;
            _mongoFallback = mongoFallback;
            _logger = logger;
        }

        private static string KeyFor(string code) =>
            IamRedisKeys.AuthorizationCode(BlocksContext.GetContext()?.TenantId, code);

        public async Task<string> CreateAsync(AuthorizationCodeModel code)
        {
            ArgumentNullException.ThrowIfNull(code);
            var key = KeyFor(code.Code);

            var transaction = _cacheClient.CacheDatabase().CreateTransaction();
            var set = transaction.HashSetAsync(key, DataField, Serialize(code));
            var expire = transaction.KeyExpireAsync(key, KeyTtl);
            if (!await transaction.ExecuteAsync())
            {
                throw new InvalidOperationException("Authorization code could not be stored.");
            }
            await Task.WhenAll(set, expire);

            _logger.LogInformation("Authorization code created for user {UserId}, client {ClientId}", code.UserId, code.ClientId);
            return code.Code;
        }

        public async Task<AuthorizationCodeModel> GetByCodeAsync(string code)
        {
            var data = await _cacheClient.CacheDatabase().HashGetAsync(KeyFor(code), DataField);
            if (data.HasValue)
            {
                var model = JsonSerializer.Deserialize<AuthorizationCodeModel>(data.ToString())!;
                model.Code = code;
                model.Source = AuthorizationCodeSource.Redis;
                return model;
            }

            var legacy = await _mongoFallback.GetByCodeAsync(code);
            if (legacy != null)
            {
                legacy.Source = AuthorizationCodeSource.Mongo;
            }
            return legacy!;
        }

        public async Task<AuthorizationCodeConsumeResult> TryConsumeAsync(AuthorizationCodeModel code)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (code.Source == AuthorizationCodeSource.Mongo)
            {
                // Codes from older pods keep today's rule: a second use is refused, nothing else.
                return await _mongoFallback.TryConsumeAsync(code.Code)
                    ? AuthorizationCodeConsumeResult.Consumed
                    : AuthorizationCodeConsumeResult.Refused;
            }

            var result = (long)await EvaluateAsync(ConsumeScript, code.Code, DateTime.UtcNow.ToString("O"));
            return result switch
            {
                1 => AuthorizationCodeConsumeResult.Consumed,
                0 => AuthorizationCodeConsumeResult.Reused,
                _ => AuthorizationCodeConsumeResult.Refused
            };
        }

        public async Task<bool> RecordMintAsync(AuthorizationCodeModel code, AuthorizationCodeMint mint)
        {
            ArgumentNullException.ThrowIfNull(code);
            ArgumentNullException.ThrowIfNull(mint);
            if (code.Source == AuthorizationCodeSource.Mongo)
            {
                return true;
            }

            var result = (long)await EvaluateAsync(MintScript, code.Code, JsonSerializer.Serialize(mint));
            return result == 1;
        }

        public async Task<AuthorizationCodeMint?> MarkReusedAsync(AuthorizationCodeModel code)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (code.Source == AuthorizationCodeSource.Mongo)
            {
                return null;
            }

            var result = await EvaluateAsync(ReuseScript, code.Code);
            if (result.Resp2Type != ResultType.BulkString || result.IsNull)
            {
                return null;
            }

            return JsonSerializer.Deserialize<AuthorizationCodeMint>(result.ToString());
        }

        private Task<RedisResult> EvaluateAsync(string script, string code, params RedisValue[] args) =>
            _cacheClient.CacheDatabase().ScriptEvaluateAsync(script, new RedisKey[] { KeyFor(code) }, args);

        /// <summary>The stored JSON never holds the raw code (the key holds only its hash).</summary>
        private static string Serialize(AuthorizationCodeModel code)
        {
            var node = JsonSerializer.SerializeToNode(code)!.AsObject();
            node.Remove(nameof(AuthorizationCodeModel.Code));
            return node.ToJsonString();
        }
    }
}
