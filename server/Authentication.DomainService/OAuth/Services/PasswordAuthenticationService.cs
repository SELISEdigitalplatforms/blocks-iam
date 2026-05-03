using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
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

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Password Authentication start");

            user = await _oAuthRepository.GetUserByUsernameAsync(request.Username, request.OrganizationId);
            if (!IsValidUser(user)) return OAuthError.InValidResponse(request);
            if (!IsUserActiveAndVerified(user)) return OAuthError.UserNotActiveOrVerifiedResponse();

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new TokenResponse
                {
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed login attempts",
                    StatusCode = 423
                };
            }

            var passwordMatched = VerifyPassword(request.Password, user.Password);

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
                        { nameof(User.LockoutUntilUtc), lockoutUntilUtc ?? (object?)null },
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });

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

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

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
    }
}
