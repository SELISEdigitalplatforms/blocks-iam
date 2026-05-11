using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.OAuth
{
    public class PasswordAuthenticationService : ITokenService
    {
        private readonly ILogger<PasswordAuthenticationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICryptoService _cryptoService;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ICacheClient _cacheClient;
        private readonly IAccountService _accountService;

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository,
            IAuthenticationDomainService authenticationDomainService,
            ICacheClient cacheClient,
            IAccountService accountService
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
            _authenticationDomainService = authenticationDomainService;
            _cacheClient = cacheClient;
            _accountService = accountService;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Password Authentication start");

            user ??= await _oAuthRepository.GetUserByUsernameAsync(request.Username, request.OrganizationId);
            if (!IsValidUser(user) || !IsUserActiveAndVerified(user!)) return OAuthError.InValidResponse(request);

            // Check IP-based rate limiting
            var clientIp = _authenticationDomainService.GetVisitorsIpAddresses(request.Request.HttpContext).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(clientIp))
            {
                var ipRateLimitCheckResult = await CheckIpRateLimitAsync(clientIp, request.Username, authenticationConfiguration);
                if (!ipRateLimitCheckResult.IsAllowed)
                {
                    _logger.LogWarning($"IP rate limit exceeded for {clientIp} attempting to login as {request.Username}. Limit: {ipRateLimitCheckResult.LimitType}");
                    await SendTimelineEventAsync(request, user.ItemId, "failed_login_ip_rate_limited", "password_auth_ip_rate_limited");
                    return new TokenResponse
                    {
                        Error = "ip_rate_limited",
                        ErrorDescription = $"Too many login attempts from your IP address. Please try again later.",
                        StatusCode = 429
                    };
                }
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                await SendTimelineEventAsync(request, user.ItemId, "failed_login_account_locked", "password_auth_account_locked");
                return new TokenResponse
                {
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed login attempts",
                    StatusCode = 423
                };
            }

            var tenantId = BlocksContext.GetContext()?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            var passwordMatched = VerifyPassword(request.Password, user.Password ?? string.Empty, tenant?.TenantSalt);

            if (!passwordMatched)
            {
                var nowUtc = DateTime.UtcNow;
                var updatedUser = await _oAuthRepository.IncrementFailedLoginAndApplyLockoutAsync(
                    user.ItemId,
                    authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount,
                    authenticationConfiguration.AccountLockDurationInMinutes,
                    nowUtc);

                var lockoutUntilUtc = updatedUser?.LockoutUntilUtc;

                var eventName = lockoutUntilUtc.HasValue
                    ? "failed_login_and_account_locked"
                    : "failed_login_invalid_password";
                var actionBy = lockoutUntilUtc.HasValue
                    ? "password_auth_lock_after_failed_attempts"
                    : "password_auth_failed_attempt";

                await SendTimelineEventAsync(request, user.ItemId, eventName, actionBy);

                // Send email notification when account is locked
                if (lockoutUntilUtc.HasValue && updatedUser != null)
                {
                    try
                    {
                        await _accountService.SendAccountLockedNotificationAsync(updatedUser, lockoutUntilUtc.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send account locked notification after failed login for user: {UserId}", user.ItemId);
                    }
                }

                return new TokenResponse { Error = OAuthError.InValidUseNamePassword, ErrorDescription = "Invalid username or password", StatusCode = 401 };
            }

            if (user.FailedLoginCount > 0 || user.LastFailedLoginUtc.HasValue || user.LockoutUntilUtc.HasValue)
            {
                await _oAuthRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.FailedLoginCount), 0 },
                        { nameof(User.LastFailedLoginUtc), null! },
                        { nameof(User.LockoutUntilUtc), null! },
                        { nameof(User.LockoutCount), 0 }, // Reset exponential backoff counter on successful login
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });
            }

            request.OrganizationId = ResolveSignInOrganizationId(user, request.OrganizationId);
            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

            if (tokenResponse != null
                && string.IsNullOrWhiteSpace(tokenResponse.Error)
                && !string.IsNullOrWhiteSpace(request.OrganizationId)
                && !string.Equals(user.LastUsedOrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                await _oAuthRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.LastUsedOrganizationId), request.OrganizationId },
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });
            }

            return tokenResponse;

        }

        private static bool IsValidUser(User? user) =>
            user != null;

        private static bool IsUserActiveAndVerified(User user) =>
            user.Active && user.IsVarified;

        public string HashPassword(string password, string? optionalSalt = null)
        {
            return BCryptNet.HashPassword(BuildPasswordMaterial(password, optionalSalt));
        }

        public bool VerifyPassword(string? password, string? passwordHash, string? optionalSalt = null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                return false;
            }

            try
            {
                return BCryptNet.Verify(BuildPasswordMaterial(password, optionalSalt), passwordHash);
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                _logger.LogWarning(ex, "Password hash is not a valid BCrypt hash format.");
                return false;
            }
        }

        private static string BuildPasswordMaterial(string password, string? optionalSalt)
        {
            return string.IsNullOrWhiteSpace(optionalSalt)
                ? password
                : $"{password}::{optionalSalt}";
        }

        private async Task SendTimelineEventAsync(TokenRequest request, string userId, string eventName, string actionBy)
        {
            if (string.IsNullOrWhiteSpace(userId) || request?.Request?.HttpContext == null)
            {
                return;
            }

            var timelineEvent = new UserAuthenticationTimelineEvent
            {
                UserId = userId,
                Event = eventName,
                ActionBy = actionBy,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(request.Request.Headers.UserAgent.ToString()),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(request.Request.HttpContext))
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
        }

        private static string ResolveSignInOrganizationId(User user, string? requestedOrganizationId)
        {
            if (HasOrganizationAccess(user, requestedOrganizationId))
            {
                return requestedOrganizationId!;
            }

            if (HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId!;
            }

            if (HasOrganizationAccess(user, "default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? user.Roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? user.Permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? "default";
        }

        private static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }

        /// <summary>
        /// Checks IP-based rate limiting for login attempts.
        /// Tracks attempts per IP per hour and per day.
        /// Returns IsAllowed=false if limit exceeded.
        /// </summary>
        private async Task<IpRateLimitResult> CheckIpRateLimitAsync(
            string clientIp,
            string username,
            AuthenticationConfiguration config)
        {
            var now = DateTime.UtcNow;
            
            // Limit keys: "login_ip_hourly:{ip}:{date-hour}", "login_ip_daily:{ip}:{date}"
            var hourlyKey = $"login_ip_hourly:{clientIp}:{now:yyyy-MM-dd-HH}";
            var dailyKey = $"login_ip_daily:{clientIp}:{now:yyyy-MM-dd}";

            // Check hourly limit (configurable, default 100 attempts)
            var hourlyAttempts = await _cacheClient.GetStringValueAsync(hourlyKey);
            var hourlyCount = !string.IsNullOrWhiteSpace(hourlyAttempts) ? int.Parse(hourlyAttempts) : 0;
            
            if (hourlyCount >= config.MaxLoginAttemptsPerIpPerHour)
            {
                return new IpRateLimitResult { IsAllowed = false, LimitType = "hourly" };
            }

            // Check daily limit (configurable, default 500 attempts)
            var dailyAttempts = await _cacheClient.GetStringValueAsync(dailyKey);
            var dailyCount = !string.IsNullOrWhiteSpace(dailyAttempts) ? int.Parse(dailyAttempts) : 0;

            if (dailyCount >= config.MaxLoginAttemptsPerIpPerDay)
            {
                return new IpRateLimitResult { IsAllowed = false, LimitType = "daily" };
            }

            // Increment counters (expiry in seconds: 3600 = 1 hour, 86400 = 1 day)
            await _cacheClient.AddStringValueAsync(hourlyKey, (hourlyCount + 1).ToString(), 3600);
            await _cacheClient.AddStringValueAsync(dailyKey, (dailyCount + 1).ToString(), 86400);

            return new IpRateLimitResult { IsAllowed = true };
        }

        private class IpRateLimitResult
        {
            public bool IsAllowed { get; set; }
            public string LimitType { get; set; } = string.Empty;
        }
    }
}
