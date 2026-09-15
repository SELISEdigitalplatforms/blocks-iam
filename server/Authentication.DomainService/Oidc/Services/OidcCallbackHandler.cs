using Iam.DomainService.Utilities;
using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    public interface IOidcCallbackHandler
    {
        Task<OidcCallbackResult> HandleCallbackAsync(string code, string state);

        /// <summary>
        /// Turns an error handed back by the social provider -- most often the user pressing
        /// Cancel on its consent screen -- into a failure that still knows which OIDC request
        /// it belongs to, so the controller can return the user to the login page they started
        /// from instead of a bare error body.
        /// </summary>
        Task<OidcCallbackResult> HandleProviderErrorAsync(string state, string providerError, string? providerErrorDescription);
    }

    public sealed class OidcCallbackResult
    {
        public bool IsSuccess { get; set; }
        public string? AccessToken { get; set; }
        public string? IdToken { get; set; }
        public string? RefreshToken { get; set; }
        public Dictionary<string, object>? TokenPayload { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Machine-readable reason a callback failed, e.g. <c>signup_disabled</c>. The callback
        /// lands in the browser, so the controller needs to tell a policy refusal (send the user
        /// back to the login screen with something to read) from a broken flow.
        /// </summary>
        public string? ErrorCode { get; set; }
        
        // OIDC flow specific fields
        public bool IsOidcFlow { get; set; } = false;
        public string? AuthorizationCode { get; set; }
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
        public string? OriginalState { get; set; }
        public string? Scope { get; set; }
        public string? Nonce { get; set; }
        public string? CodeChallenge { get; set; }
        public string? CodeChallengeMethod { get; set; }
        
        // Session/User identification for post-callback setup
        public string? BlocksUserId { get; set; }
        public string? TenantId { get; set; }
    }

    public sealed class OidcCallbackHandler : IOidcCallbackHandler
    {
        private readonly ILogger<OidcCallbackHandler> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        private readonly ISsoUserProvisioningService _ssoUserProvisioningService;

        public OidcCallbackHandler(
            ILogger<OidcCallbackHandler> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            ISsoUserProvisioningService ssoUserProvisioningService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
            _ssoUserProvisioningService = ssoUserProvisioningService;
        }

        public async Task<OidcCallbackResult> HandleCallbackAsync(string code, string state)
        {
            try
            {
                // Check if this is part of OIDC social flow (state is oidc_social_state)
                var oidcSocialStateJson = await _cacheClient.GetStringValueAsync($"oidc_social_state:{state}");
                
                if (string.IsNullOrWhiteSpace(oidcSocialStateJson))
                {
                    _logger.LogWarning("Invalid OIDC state: {State}", state);
                    return new OidcCallbackResult { IsSuccess = false, ErrorCode = "invalid_state", ErrorMessage = "Invalid or expired OIDC state" };
                }

                // OIDC SOCIAL FLOW - User authenticated via social provider within OIDC
                // Process and return authorization code for original OIDC client
                return await HandleOidcSocialCallbackAsync(code, state, oidcSocialStateJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC callback");
                return new OidcCallbackResult { IsSuccess = false, ErrorCode = "server_error", ErrorMessage = "An error occurred during token exchange" };
            }
        }


        public async Task<OidcCallbackResult> HandleProviderErrorAsync(string state, string providerError, string? providerErrorDescription)
        {
            var errorCode = string.IsNullOrWhiteSpace(providerError) ? "access_denied" : providerError;
            var message = string.IsNullOrWhiteSpace(providerErrorDescription)
                ? DescribeProviderError(errorCode)
                : providerErrorDescription!;

            OidcContext? context = null;

            try
            {
                context = await ResolveOidcContextAsync(state);
            }
            catch (Exception ex)
            {
                // The provider's error is the thing worth telling the user about. Failing to look
                // up the context only costs them the nicer landing place, so it must not replace
                // the reason they were sent back here.
                _logger.LogError(ex, "Error resolving OIDC context for a provider error callback");
            }

            if (context == null)
            {
                _logger.LogWarning("Provider returned {Error} for state {State}, with no recoverable OIDC context", errorCode, state);
            }

            return new OidcCallbackResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = message,
                IsOidcFlow = true,
                ClientId = context?.ClientId,
                RedirectUri = context?.RedirectUri,
                OriginalState = context?.State,
                Scope = context?.Scope,
                Nonce = context?.Nonce,
                CodeChallenge = context?.CodeChallenge,
                CodeChallengeMethod = context?.CodeChallengeMethod,
                TenantId = context?.TenantId
            };
        }

        /// <summary>
        /// Walks the same two cache hops the social callback does -- the social state, then the
        /// OIDC request it wraps -- without consuming either. Returns null at the first miss;
        /// every miss means the same thing to the caller, that there is no request left to send
        /// the user back to.
        /// </summary>
        private async Task<OidcContext?> ResolveOidcContextAsync(string state)
        {
            var socialStateJson = await _cacheClient.GetStringValueAsync($"oidc_social_state:{state}");
            if (string.IsNullOrWhiteSpace(socialStateJson))
            {
                return null;
            }

            var socialState = JsonSerializer.Deserialize<OidcSocialStateContext>(socialStateJson);
            if (socialState == null || string.IsNullOrWhiteSpace(socialState.OidcState))
            {
                return null;
            }

            var contextJson = await _cacheClient.GetStringValueAsync($"oidc_context:{socialState.OidcState}");
            if (string.IsNullOrWhiteSpace(contextJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<OidcContext>(contextJson);
        }

        /// <summary>
        /// Plain-language wording for the error codes providers send when they refuse, used only
        /// when the provider supplies no description of its own. The user reads this in a dialog,
        /// so it says what happened rather than repeating the code.
        /// </summary>
        private static string DescribeProviderError(string providerError)
        {
            return providerError switch
            {
                "access_denied" => "Sign-in was cancelled before it finished. You can try again, or use a different sign-in method.",
                "consent_required" => "The provider needs you to grant permission before you can sign in this way.",
                "login_required" => "The provider needs you to sign in again before it can complete this request.",
                "temporarily_unavailable" => "The sign-in provider is temporarily unavailable. Please try again in a moment.",
                _ => "The sign-in provider could not complete this request. You can try again, or use a different sign-in method."
            };
        }

        /// <summary>
        /// Handle OIDC social login - issues authorization code for original OIDC client
        /// Flow: Frontend → OIDC Server → Social Provider → Backend → Issue Auth Code → Frontend
        /// </summary>
        private async Task<OidcCallbackResult> HandleOidcSocialCallbackAsync(string code, string state, string oidcSocialStateJson)
        {
            // Lives outside the try so every failure path -- the catch included -- can hand the
            // original OIDC request back to the controller. This callback is a browser redirect,
            // not a fetch: without the request context there is nowhere to send the user except
            // a bare error body rendered in the address bar.
            OidcContext? recoveredContext = null;

            OidcCallbackResult Failure(string errorCode, string message) => new()
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = message,
                IsOidcFlow = true,
                ClientId = recoveredContext?.ClientId,
                RedirectUri = recoveredContext?.RedirectUri,
                OriginalState = recoveredContext?.State,
                Scope = recoveredContext?.Scope,
                Nonce = recoveredContext?.Nonce,
                CodeChallenge = recoveredContext?.CodeChallenge,
                CodeChallengeMethod = recoveredContext?.CodeChallengeMethod,
                TenantId = recoveredContext?.TenantId
            };

            try
            {
                // Parse OIDC social state to get context
                var oidcSocialState = JsonSerializer.Deserialize<OidcSocialStateContext>(oidcSocialStateJson);
                if (oidcSocialState == null || string.IsNullOrWhiteSpace(oidcSocialState.OidcState))
                {
                    _logger.LogWarning("Invalid OIDC state in social callback");
                    return Failure("invalid_request", "Invalid OIDC context");
                }

                if (string.IsNullOrWhiteSpace(oidcSocialState.Provider))
                {
                    _logger.LogWarning("Provider not found in OIDC social state");
                    return Failure("invalid_request", "Provider not found in OIDC state");
                }

                var oidcState = oidcSocialState.OidcState;
                var provider = oidcSocialState.Provider;

                // 1. Get OIDC context (original client request)
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                {
                    _logger.LogWarning("OIDC context not found for state {OidcState}", oidcState);
                    return Failure("oidc_flow_expired", "OIDC flow expired");
                }

                var context = JsonSerializer.Deserialize<OidcContext>(contextJson);
                if (context == null)
                {
                    _logger.LogWarning("Failed to deserialize OIDC context for state {OidcState}", oidcState);
                    return Failure("oidc_flow_expired", "OIDC flow expired");
                }

                // From here on a failure can be shown on the login page the user started from.
                recoveredContext = context;

                // 3. Reuse social folder callback handling for provider token exchange + user extraction
                var stateInfo = new StateInfo
                {
                    ClientId = context.ProviderClientId,
                    Provider = provider,
                    Code = code,
                    Audience = context.ProviderClientId,
                    RedirectUri = context.ProviderRedirectUri,
                    FlowType = SocialFlowType.Oidc
                };

                var socialCallbackResult = await _socialLogInServiceProvider.HandleSocialLoginCallback(stateInfo);
                var externalUserData = socialCallbackResult.ExternalUserData;

                if (externalUserData == null || string.IsNullOrWhiteSpace(externalUserData.Email))
                {
                    _logger.LogError("Social provider callback did not return user email for provider {Provider}", provider);
                    return Failure("provider_email_missing", "Provider did not return a valid user email");
                }

                // 5. Resolve the Blocks user behind the external identity, provisioning one when
                // the tenant allows SSO signup. Shared with /social/callback so the two callbacks
                // cannot drift apart again.
                var provisioning = await _ssoUserProvisioningService.ResolveOrProvisionAsync(externalUserData, provider);

                if (provisioning.Outcome == SsoProvisioningOutcome.SignupDisabled)
                {
                    _logger.LogWarning("SSO signup is disabled for this tenant; refusing unknown user from {Provider}", provider);
                    return Failure(
                        "signup_disabled",
                        "No account exists for this email, and signing up with SSO is turned off. Contact your administrator to get access.");
                }

                var ssoUser = provisioning.User;

                if (ssoUser == null || string.IsNullOrWhiteSpace(ssoUser.ItemId))
                {
                    _logger.LogError("Failed to create/update user for provider {Provider}", provider);
                    return Failure("user_provisioning_failed", "Failed to create user account. Please try again.");
                }

                if (!ssoUser.Active)
                {
                    _logger.LogError("User with id {UserId} is not active for provider {Provider}", ssoUser.ItemId, provider);
                    return Failure("user_inactive", "This account is not active. Contact your administrator to get access.");
                }

                // 6. Clean up temporary states. The actual authorization code must be created
                // through AuthorizationFlowService.AuthorizeAsync so it persists as AuthorizationCodeModel.
                await _cacheClient.RemoveKeyAsync($"oidc_social_state:{state}");
                await _cacheClient.RemoveKeyAsync(contextKey);

                // 7. Return the recovered OIDC request context so the controller can continue
                // through the same AuthorizeAsync path used by password-based OIDC login.
                return new OidcCallbackResult
                {
                    IsSuccess = true,
                    ClientId = context.ClientId,
                    RedirectUri = context.RedirectUri,
                    OriginalState = context.State,
                    IsOidcFlow = true,
                    BlocksUserId = ssoUser.ItemId,
                    TenantId = string.IsNullOrWhiteSpace(context.TenantId) ? "default" : context.TenantId,
                    Scope = context.Scope,
                    Nonce = context.Nonce,
                    CodeChallenge = context.CodeChallenge,
                    CodeChallengeMethod = context.CodeChallengeMethod
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC social callback for provider");
                return Failure("server_error", "An error occurred during OIDC processing");
            }
        }

    }
}
