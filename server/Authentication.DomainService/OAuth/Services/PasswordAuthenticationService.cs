using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.OAuth
{
    public sealed class PasswordAuthenticationService : ITokenService
    {
        private readonly ILogger<PasswordAuthenticationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICryptoService _cryptoService;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IAccountService _accountService;

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository,
            IAuthenticationDomainService authenticationDomainService,
            IAccountService accountService
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
            _authenticationDomainService = authenticationDomainService;
            _accountService = accountService;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Password Authentication start");

            // INVARIANT: All login failure modes (user not found, inactive, not verified)
            // MUST return the generic `OAuthError.InValidResponse(request)` shape. The client
            // only ever sees `invalid_username_password` / 401. Do not leak discriminators
            // such as "user is not active" or "user not verified" — see
            // PasswordAuthenticationServiceInvariantTests for regression coverage.
            user ??= await _oAuthRepository.GetUserByUsernameAsync(request.Username, request.OrganizationId);
            if (!IsValidUser(user) || !IsUserActiveAndVerified(user!)) return OAuthError.InValidResponse(request);

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

            request.OrganizationId = ResolveSignInOrganizationId(user, request.OrganizationId);
            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

            if (tokenResponse != null
                && string.IsNullOrWhiteSpace(tokenResponse.Error)
                && (user.FailedLoginCount > 0
                    || user.LastFailedLoginUtc.HasValue
                    || user.FailedMfaCount > 0
                    || user.LastFailedMfaUtc.HasValue
                    || user.LockoutUntilUtc.HasValue))
            {
                await _oAuthRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.FailedLoginCount), 0 },
                        { nameof(User.LastFailedLoginUtc), null! },
                        { nameof(User.FailedMfaCount), 0 },
                        { nameof(User.LastFailedMfaUtc), null! },
                        { nameof(User.LockoutUntilUtc), null! },
                        { nameof(User.LockoutCount), 0 }, // Reset exponential backoff counter on successful login
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });
            }

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

        // INVARIANT: Both `IsValidUser` and `IsUserActiveAndVerified` are combined in
        // `AuthenticateAsync` to collapse "user not found", "inactive", and "not verified"
        // into the same generic `OAuthError.InValidResponse`. Returning a different error
        // for any of these branches leaks an account-state discriminator. The lockout path
        // (returning 423) is an intentional, separate response and not a security leak
        // because the user already knows whether their own account is locked.
        private static bool IsValidUser(User? user) =>
            user != null;

        private static bool IsUserActiveAndVerified(User user) =>
            user.Active && user.IsVerified;

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

    }
}
