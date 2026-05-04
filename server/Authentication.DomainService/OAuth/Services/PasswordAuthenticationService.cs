using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using BCryptNet = BCrypt.Net.BCrypt;

namespace DomainService.OAuth
{
    public class PasswordAuthenticationService : ITokenService
    {
        private readonly ILogger<PasswordAuthenticationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICryptoService _cryptoService;
        private readonly IAuthenticationDomainService _authenticationDomainService;

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository,
            IAuthenticationDomainService authenticationDomainService
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
            _authenticationDomainService = authenticationDomainService;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Password Authentication start");

            user = await _oAuthRepository.GetUserByUsernameAsync(request.Username, request.OrganizationId);
            if (!IsValidUser(user)) return OAuthError.InValidResponse(request);
            if (!IsUserActiveAndVerified(user)) return OAuthError.UserNotActiveOrVerifiedResponse();

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

            var passwordMatched = VerifyPassword(request.Password, user.Password ?? string.Empty);

            if (!passwordMatched)
            {
                var nextFailedCount = user.FailedLoginCount + 1;
                DateTime? lockoutUntilUtc = null;
                if (nextFailedCount >= authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount)
                {
                    lockoutUntilUtc = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccountLockDurationInMinutes);
                }

                await _oAuthRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.FailedLoginCount), nextFailedCount },
                        { nameof(User.LastFailedLoginUtc), DateTime.UtcNow },
                        { nameof(User.LockoutUntilUtc), lockoutUntilUtc ?? (object)DBNull.Value },
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });

                var eventName = lockoutUntilUtc.HasValue
                    ? "failed_login_and_account_locked"
                    : "failed_login_invalid_password";
                var actionBy = lockoutUntilUtc.HasValue
                    ? "password_auth_lock_after_failed_attempts"
                    : "password_auth_failed_attempt";

                await SendTimelineEventAsync(request, user.ItemId, eventName, actionBy);

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

        private static bool IsValidUser(User user) =>
            user != null;

        private static bool IsUserActiveAndVerified(User user) =>
            user.Active && user.IsVarified;

        public string HashPassword(string password, string? optionalSalt = null)
        {
            return BCryptNet.HashPassword(BuildPasswordMaterial(password, optionalSalt));
        }

        public bool VerifyPassword(string password, string passwordHash, string? optionalSalt = null)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                return false;
            }

            return BCryptNet.Verify(BuildPasswordMaterial(password, optionalSalt), passwordHash);
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
