using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Services;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Authentication.DomainService.Authentication;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.OAuth
{
    public sealed class PasswordAuthenticationService : ITokenService
    {
        private readonly ILogger<PasswordAuthenticationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICryptoService _cryptoService;
        private readonly IPasswordCredentialVerifier _passwordVerifier;

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository,
            IAuthenticationDomainService authenticationDomainService,
            IAccountService accountService,
            IUserActivityDispatcher userActivityDispatcher,
            IPasswordCredentialVerifier? passwordVerifier = null
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
            _passwordVerifier = passwordVerifier ?? new PasswordCredentialVerifier(
                NullLogger<PasswordCredentialVerifier>.Instance,
                tenants,
                oAuthRepository,
                accountService,
                userActivityDispatcher,
                authenticationDomainService);
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

            // Lockout pre-check, password and failed-attempt accounting live in the shared
            // verifier, so the signup-link password step runs the same code. Login maps every
            // failure to the response it has always returned: 423 locked, otherwise 401.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            var verification = await _passwordVerifier.VerifyPasswordAsync(
                user, request.Password, authenticationConfiguration, request.Request, tenantId);
            if (!verification.Succeeded)
            {
                return new TokenResponse
                {
                    Error = verification.Error,
                    ErrorDescription = verification.ErrorDescription,
                    StatusCode = verification.StatusCode
                };
            }

            request.OrganizationId = OrganizationAccessResolver.ResolveSignInOrganizationId(user, request.OrganizationId);
            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

            if (tokenResponse != null && string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                await _passwordVerifier.ResetFailureCountersAsync(user);
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

        public bool VerifyPassword(string? password, string? passwordHash, string? optionalSalt = null) =>
            _passwordVerifier.VerifyPassword(password, passwordHash, optionalSalt);

        private static string BuildPasswordMaterial(string password, string? optionalSalt) =>
            PasswordCredentialVerifier.BuildPasswordMaterial(password, optionalSalt);
    }
}
