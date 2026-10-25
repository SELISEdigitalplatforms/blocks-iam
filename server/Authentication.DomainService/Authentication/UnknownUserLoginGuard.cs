using System.Text;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Makes a password login for a username with no usable account (unknown, pending, inactive,
    /// disabled) answer like a wrong password for a real account: the same BCrypt work, and the
    /// same captcha and lock signals at the same thresholds. A real account keeps its state on the
    /// user document; here it lives in one Redis hash per tenant and normalized username, written
    /// by one Lua script that mirrors <see cref="IAuthenticationRepository.TryReserveLoginAttemptAsync"/>
    /// and <see cref="IAuthenticationRepository.LockAccountIfLoginThresholdReachedAsync"/>.
    /// </summary>
    public interface IUnknownUserLoginGuard
    {
        /// <summary>Failed attempts counted so far, and whether the name is locked right now.</summary>
        Task<UnknownUserLoginState> GetStateAsync(string? username);

        /// <summary>
        /// One wrong-password attempt: takes an attempt slot, runs a BCrypt check against a dummy
        /// hash, counts the failure and locks once the tenant's threshold is reached.
        /// </summary>
        Task<UnknownUserLoginAttempt> FailAsync(string? username, string? password, string? tenantSalt, IdentityConfiguration configuration);
    }

    public readonly record struct UnknownUserLoginState(int FailedCount, bool Locked);

    /// <param name="Reserved">False when no slot was free: answer as locked, like a failed reservation.</param>
    /// <param name="LockedNow">This attempt reached the threshold and locked the name.</param>
    public readonly record struct UnknownUserLoginAttempt(bool Reserved, bool LockedNow);

    public sealed class UnknownUserLoginGuard : IUnknownUserLoginGuard
    {
        /// <summary>
        /// How long the state lives after the last failure. Longer than any lock it sets, so the
        /// captcha and the one-strike re-lock after a lock behave like a real account's.
        /// </summary>
        public static readonly TimeSpan StateLifetime = TimeSpan.FromHours(24);

        /// <summary>Same reset window the account lock uses (AuthenticationRepository, 7 days).</summary>
        private static readonly TimeSpan LockResetWindow = TimeSpan.FromDays(7);

        /// <summary>
        /// Made once per process with the library's default work factor, the same one every stored
        /// password hash uses, so verifying against it costs what a real check costs.
        /// </summary>
        private static readonly Lazy<string> Dummy = new(() => BCryptNet.HashPassword(Guid.NewGuid().ToString("N")));

        // Fields: c = failed count, f = last failure (ms), u = locked until (ms), n = locks so far, l = last lock (ms).
        // Returns {count, lockedNow}, or {-1, 0} when no slot is free (locked, or the last slot is taken).
        private const string FailScript = @"
local now = tonumber(ARGV[1])
local threshold = tonumber(ARGV[2])
local lease = tonumber(ARGV[3])
local baseMs = tonumber(ARGV[4])
local resetMs = tonumber(ARGV[5])
local lifeMs = tonumber(ARGV[6])
local h = redis.call('HMGET', KEYS[1], 'c', 'f', 'u', 'n', 'l')
local c = tonumber(h[1]) or 0
local f = tonumber(h[2]) or 0
local u = tonumber(h[3]) or 0
local n = tonumber(h[4]) or 0
local l = tonumber(h[5]) or 0
if u > now then return {-1, 0} end
if c >= threshold and (now - f) < lease then return {-1, 0} end
c = c + 1
redis.call('HSET', KEYS[1], 'c', c, 'f', now)
local lockedNow = 0
local lockedUntil = now
if c >= threshold then
  local mult = 1
  if l > 0 and (now - l) >= resetMs then mult = 1
  elseif n == 1 then mult = 3
  elseif n == 2 then mult = 12
  elseif n >= 3 then mult = 288 end
  lockedUntil = now + baseMs * mult
  redis.call('HSET', KEYS[1], 'u', lockedUntil, 'n', n + 1, 'l', now)
  lockedNow = 1
end
redis.call('PEXPIRE', KEYS[1], (lockedUntil - now) + lifeMs)
return {c, lockedNow}";

        private readonly ICacheClient _cacheClient;
        private readonly ILogger<UnknownUserLoginGuard> _logger;

        public UnknownUserLoginGuard(ICacheClient cacheClient, ILogger<UnknownUserLoginGuard> logger)
        {
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<UnknownUserLoginState> GetStateAsync(string? username)
        {
            try
            {
                var values = await _cacheClient.CacheDatabase().HashGetAsync(KeyFor(username), new RedisValue[] { "c", "u" });
                var count = values.Length > 0 && values[0].TryParse(out long c) ? (int)c : 0;
                var lockedUntil = values.Length > 1 && values[1].TryParse(out long u) ? u : 0;
                return new UnknownUserLoginState(count, lockedUntil > NowMs());
            }
            catch (Exception ex)
            {
                // Fail open: the answer stays the generic one, only the extra signals are missing.
                _logger.LogWarning(ex, "Unknown-user login state could not be read");
                return new UnknownUserLoginState(0, false);
            }
        }

        public async Task<UnknownUserLoginAttempt> FailAsync(string? username, string? password, string? tenantSalt, IdentityConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            UnknownUserLoginAttempt attempt;
            try
            {
                var result = (RedisResult[])(await _cacheClient.CacheDatabase().ScriptEvaluateAsync(
                    FailScript,
                    new RedisKey[] { KeyFor(username) },
                    new RedisValue[]
                    {
                        NowMs(),
                        configuration.GetNumberOfWrongAttemptsToLockTheAccount,
                        (long)AuthenticationRepository.LoginAttemptLease.TotalMilliseconds,
                        (long)TimeSpan.FromMinutes(configuration.AccountLockDurationInMinutes).TotalMilliseconds,
                        (long)LockResetWindow.TotalMilliseconds,
                        (long)StateLifetime.TotalMilliseconds
                    }))!;
                var count = (long)result[0];
                attempt = count < 0
                    ? new UnknownUserLoginAttempt(false, false)
                    : new UnknownUserLoginAttempt(true, (long)result[1] == 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unknown-user login attempt could not be counted");
                attempt = new UnknownUserLoginAttempt(true, false);
            }

            // A real account runs BCrypt only once it holds a slot; do the same.
            if (attempt.Reserved)
            {
                VerifyAgainstDummy(password, tenantSalt);
            }

            return attempt;
        }

        /// <summary>
        /// A full BCrypt verify that always fails. Used wherever there is no stored hash to check,
        /// so the time taken does not tell whether an account (or a password on it) exists.
        /// A blank password is refused before BCrypt on the real path too, so it is skipped here.
        /// </summary>
        public static bool VerifyAgainstDummy(string? password, string? optionalSalt)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            BCryptNet.Verify(PasswordCredentialVerifier.BuildPasswordMaterial(password, optionalSalt), Dummy.Value);
            return false;
        }

        /// <summary>
        /// Same identity the user lookup matches (trimmed, case-insensitive), plus Unicode
        /// compatibility folding so full-width and other look-alike forms share one counter, as the
        /// lookup's case- and width-insensitive collation makes them share one account.
        /// </summary>
        public static string NormalizeUsername(string? username) =>
            string.IsNullOrWhiteSpace(username)
                ? string.Empty
                : username.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

        private static string KeyFor(string? username) =>
            IamRedisKeys.UnknownLogin(BlocksContext.GetContext()?.TenantId, NormalizeUsername(username));

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
