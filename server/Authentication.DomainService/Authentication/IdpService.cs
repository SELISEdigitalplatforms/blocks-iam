using Authentication.DomainService.Entities;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Utilities;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// IDP Service
    /// Manages identity provider authentication flow, token exchange, and OIDC operations
    /// </summary>
    public sealed class IdpService : IIdpService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IAuthenticationFlowService _authenticationFlowService;
        private readonly ICacheClient _cacheClient;
        private readonly IdpTokenExchangeClient _idpTokenExchangeClient;
        private readonly ITenants _tenants;
        private readonly ICaptchaConfigurationRepository _captchaConfigurationRepository;
        private readonly ILogger<IdpService> _logger;

        public IdpService(
            IAuthenticationRepository authenticationRepository,
            IAuthorizationCodeRepository authCodeRepo,
            IAuthenticationFlowService authenticationFlowService,
            ICacheClient cacheClient,
            IdpTokenExchangeClient idpTokenExchangeClient,
            ITenants tenants,
            ICaptchaConfigurationRepository captchaConfigurationRepository,
            ILogger<IdpService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _authCodeRepo = authCodeRepo;
            _authenticationFlowService = authenticationFlowService;
            _cacheClient = cacheClient;
            _idpTokenExchangeClient = idpTokenExchangeClient;
            _tenants = tenants;
            _captchaConfigurationRepository = captchaConfigurationRepository;
            _logger = logger;
        }

        public async Task<IActionResult> GetUiConfigAsync()
        {
            var captchaConfiguration = await _captchaConfigurationRepository.GetCaptchaConfigurationAsync();
            var savedTemplate = await _authenticationRepository.GetOidcUiTemplateAsync();

            return new OkObjectResult(new OidcUiConfigResponse
            {
                Captcha = captchaConfiguration == null || !captchaConfiguration.IsEnable ? null : new OidcUiCaptchaResponse
                {
                    Key = captchaConfiguration.CaptchaKey,
                    Provider = captchaConfiguration.Provider,
                    Generator = captchaConfiguration.CaptchaGenerator
                },
                Template = savedTemplate is null
                    ? null
                    : MergeOidcUiTemplateWithDefaults(savedTemplate)
            });
        }

        /// <summary>
        /// Creates the complete, dependency-free template that reproduces the current OIDC UI.
        /// </summary>
        public static OidcUiTemplate CreateDefaultOidcUiTemplate()
        {
            return new OidcUiTemplate
            {
                SchemaVersion = OidcUiTemplate.CurrentSchemaVersion,
                Branding = new OidcUiTemplateBranding
                {
                    LogoUrl = null,
                    BrandName = "Blocks IAM"
                },
                Theme = new OidcUiTemplateTheme
                {
                    Light = new OidcUiThemePalette
                    {
                        Primary = "#0066b2",
                        Secondary = "#0084d4",
                        Background = "#f5f7fb",
                        Surface = "#ffffff",
                        Text = "#0c1024",
                        MutedText = "#5b6378",
                        Success = "#16a34a",
                        Danger = "#dc2626",
                        Border = "#dde2ec",
                        BorderStrong = "rgba(0, 102, 178, 0.45)",
                        AccentSoft = "rgba(0, 102, 178, 0.08)"
                    },
                    Dark = new OidcUiThemePalette
                    {
                        Primary = "#0066b2",
                        Secondary = "#00b2ff",
                        Background = "#050510",
                        Surface = "#0a0a1a",
                        Text = "#e8e8f0",
                        MutedText = "#5e5e7a",
                        Success = "#17a34a",
                        Danger = "#f87171",
                        Border = "#16162a",
                        BorderStrong = "rgba(0, 102, 178, 0.35)",
                        AccentSoft = "rgba(0, 102, 178, 0.10)"
                    }
                },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage
                    {
                        Heading = "Sign in to continue to your application",
                        EmailLabel = "Work Email",
                        PasswordLabel = "Password",
                        ForgotPasswordLink = "Forgot?",
                        SubmitButton = "Login",
                        SignupPrompt = "Not a member?",
                        SignupLink = "Create an account",
                        ActivationErrorTitle = "Account Not Verified",
                        ActivationErrorMessage = "Your account needs to be activated. Check your email for the activation link.",
                        ActivateAccountButton = "Activate Account",
                        BackToLoginButton = "Back to Login"
                    },
                    Signup = new OidcUiSignupPage
                    {
                        Heading = "Create Your Blocks Account",
                        FirstNameLabel = "First Name",
                        LastNameLabel = "Last Name",
                        EmailLabel = "Work Email",
                        SubmitButton = "Create Account",
                        TermsPrefix = "I agree to the",
                        TermsLinkText = "Terms of Service",
                        PrivacyLinkText = "Privacy Policy",
                        LoginPrompt = "Already a member?",
                        LoginLink = "Sign in",
                        SuccessTitle = "Account Created",
                        SuccessSubtitle = "Check your inbox for the activation link…"
                    },
                    ForgotPassword = new OidcUiForgotPasswordPage
                    {
                        Heading = "Reset Password",
                        EmailLabel = "Email",
                        SubmitButton = "Send Recovery Link"
                    },
                    ResetPassword = new OidcUiResetPasswordPage
                    {
                        Heading = "Set a new password",
                        PasswordLabel = "New Password",
                        ConfirmPasswordLabel = "Confirm Password",
                        LogoutFromDevicesLabel = "Logout from all devices",
                        SubmitButton = "Set Password",
                        SuccessTitle = "Password Updated",
                        SuccessSubtitle = "Your password has been reset successfully."
                    },
                    Activation = new OidcUiActivationPage
                    {
                        Heading = "Activate Your Account",
                        PasswordLabel = "Password",
                        ConfirmPasswordLabel = "Confirm Password",
                        SubmitButton = "Activate",
                        SuccessTitle = "Account Activated",
                        SuccessSubtitle = "Your account is ready to use."
                    },
                    Mfa = new OidcUiMfaPage
                    {
                        Heading = "Verify it's you",
                        SubmitButton = "Verify",
                        ResendButton = "Resend Code"
                    },
                    AccountSelector = new OidcUiAccountSelectorPage
                    {
                        Heading = "Blocks IAM",
                        Subheading = "Select Account"
                    },
                    Shared = new OidcUiSharedPage
                    {
                        FooterText = "© {year} SELISE Digital Platforms. All rights reserved."
                    }
                }
            };
        }

        /// <summary>
        /// Returns a new effective template, choosing the saved value at each leaf and the
        /// compiled-in value whenever that saved leaf is null or absent.
        /// </summary>
        public static OidcUiTemplate MergeOidcUiTemplateWithDefaults(OidcUiTemplate? saved)
        {
            var defaults = CreateDefaultOidcUiTemplate();

            return new OidcUiTemplate
            {
                ItemId = saved?.ItemId,
                SchemaVersion = OidcUiTemplate.CurrentSchemaVersion,
                Branding = new OidcUiTemplateBranding
                {
                    LogoUrl = saved?.Branding?.LogoUrl ?? defaults.Branding!.LogoUrl,
                    BrandName = saved?.Branding?.BrandName ?? defaults.Branding!.BrandName
                },
                Theme = new OidcUiTemplateTheme
                {
                    Light = MergeThemePalette(saved?.Theme?.Light, defaults.Theme!.Light!),
                    Dark = MergeThemePalette(
                        saved?.Theme?.Dark ?? CreateLegacyThemePalette(saved?.Theme),
                        defaults.Theme.Dark!)
                },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage
                    {
                        Heading = saved?.Pages?.Login?.Heading ?? defaults.Pages!.Login!.Heading,
                        EmailLabel = saved?.Pages?.Login?.EmailLabel ?? defaults.Pages!.Login!.EmailLabel,
                        PasswordLabel = saved?.Pages?.Login?.PasswordLabel ?? defaults.Pages!.Login!.PasswordLabel,
                        ForgotPasswordLink = saved?.Pages?.Login?.ForgotPasswordLink ?? defaults.Pages!.Login!.ForgotPasswordLink,
                        SubmitButton = saved?.Pages?.Login?.SubmitButton ?? defaults.Pages!.Login!.SubmitButton,
                        SignupPrompt = saved?.Pages?.Login?.SignupPrompt ?? defaults.Pages!.Login!.SignupPrompt,
                        SignupLink = saved?.Pages?.Login?.SignupLink ?? defaults.Pages!.Login!.SignupLink,
                        ActivationErrorTitle = saved?.Pages?.Login?.ActivationErrorTitle ?? defaults.Pages!.Login!.ActivationErrorTitle,
                        ActivationErrorMessage = saved?.Pages?.Login?.ActivationErrorMessage ?? defaults.Pages!.Login!.ActivationErrorMessage,
                        ActivateAccountButton = saved?.Pages?.Login?.ActivateAccountButton ?? defaults.Pages!.Login!.ActivateAccountButton,
                        BackToLoginButton = saved?.Pages?.Login?.BackToLoginButton ?? defaults.Pages!.Login!.BackToLoginButton
                    },
                    Signup = new OidcUiSignupPage
                    {
                        Heading = saved?.Pages?.Signup?.Heading ?? defaults.Pages!.Signup!.Heading,
                        FirstNameLabel = saved?.Pages?.Signup?.FirstNameLabel ?? defaults.Pages!.Signup!.FirstNameLabel,
                        LastNameLabel = saved?.Pages?.Signup?.LastNameLabel ?? defaults.Pages!.Signup!.LastNameLabel,
                        EmailLabel = saved?.Pages?.Signup?.EmailLabel ?? defaults.Pages!.Signup!.EmailLabel,
                        SubmitButton = saved?.Pages?.Signup?.SubmitButton ?? defaults.Pages!.Signup!.SubmitButton,
                        TermsPrefix = saved?.Pages?.Signup?.TermsPrefix ?? defaults.Pages!.Signup!.TermsPrefix,
                        TermsLinkText = saved?.Pages?.Signup?.TermsLinkText ?? defaults.Pages!.Signup!.TermsLinkText,
                        PrivacyLinkText = saved?.Pages?.Signup?.PrivacyLinkText ?? defaults.Pages!.Signup!.PrivacyLinkText,
                        LoginPrompt = saved?.Pages?.Signup?.LoginPrompt ?? defaults.Pages!.Signup!.LoginPrompt,
                        LoginLink = saved?.Pages?.Signup?.LoginLink ?? defaults.Pages!.Signup!.LoginLink,
                        SuccessTitle = saved?.Pages?.Signup?.SuccessTitle ?? defaults.Pages!.Signup!.SuccessTitle,
                        SuccessSubtitle = saved?.Pages?.Signup?.SuccessSubtitle ?? defaults.Pages!.Signup!.SuccessSubtitle
                    },
                    ForgotPassword = new OidcUiForgotPasswordPage
                    {
                        Heading = saved?.Pages?.ForgotPassword?.Heading ?? defaults.Pages!.ForgotPassword!.Heading,
                        EmailLabel = saved?.Pages?.ForgotPassword?.EmailLabel ?? defaults.Pages!.ForgotPassword!.EmailLabel,
                        SubmitButton = saved?.Pages?.ForgotPassword?.SubmitButton ?? defaults.Pages!.ForgotPassword!.SubmitButton
                    },
                    ResetPassword = new OidcUiResetPasswordPage
                    {
                        Heading = saved?.Pages?.ResetPassword?.Heading ?? defaults.Pages!.ResetPassword!.Heading,
                        PasswordLabel = saved?.Pages?.ResetPassword?.PasswordLabel ?? defaults.Pages!.ResetPassword!.PasswordLabel,
                        ConfirmPasswordLabel = saved?.Pages?.ResetPassword?.ConfirmPasswordLabel ?? defaults.Pages!.ResetPassword!.ConfirmPasswordLabel,
                        LogoutFromDevicesLabel = saved?.Pages?.ResetPassword?.LogoutFromDevicesLabel ?? defaults.Pages!.ResetPassword!.LogoutFromDevicesLabel,
                        SubmitButton = saved?.Pages?.ResetPassword?.SubmitButton ?? defaults.Pages!.ResetPassword!.SubmitButton,
                        SuccessTitle = saved?.Pages?.ResetPassword?.SuccessTitle ?? defaults.Pages!.ResetPassword!.SuccessTitle,
                        SuccessSubtitle = saved?.Pages?.ResetPassword?.SuccessSubtitle ?? defaults.Pages!.ResetPassword!.SuccessSubtitle
                    },
                    Activation = new OidcUiActivationPage
                    {
                        Heading = saved?.Pages?.Activation?.Heading ?? defaults.Pages!.Activation!.Heading,
                        PasswordLabel = saved?.Pages?.Activation?.PasswordLabel ?? defaults.Pages!.Activation!.PasswordLabel,
                        ConfirmPasswordLabel = saved?.Pages?.Activation?.ConfirmPasswordLabel ?? defaults.Pages!.Activation!.ConfirmPasswordLabel,
                        SubmitButton = saved?.Pages?.Activation?.SubmitButton ?? defaults.Pages!.Activation!.SubmitButton,
                        SuccessTitle = saved?.Pages?.Activation?.SuccessTitle ?? defaults.Pages!.Activation!.SuccessTitle,
                        SuccessSubtitle = saved?.Pages?.Activation?.SuccessSubtitle ?? defaults.Pages!.Activation!.SuccessSubtitle
                    },
                    Mfa = new OidcUiMfaPage
                    {
                        Heading = saved?.Pages?.Mfa?.Heading ?? defaults.Pages!.Mfa!.Heading,
                        SubmitButton = saved?.Pages?.Mfa?.SubmitButton ?? defaults.Pages!.Mfa!.SubmitButton,
                        ResendButton = saved?.Pages?.Mfa?.ResendButton ?? defaults.Pages!.Mfa!.ResendButton
                    },
                    AccountSelector = new OidcUiAccountSelectorPage
                    {
                        Heading = saved?.Pages?.AccountSelector?.Heading ?? defaults.Pages!.AccountSelector!.Heading,
                        Subheading = saved?.Pages?.AccountSelector?.Subheading ?? defaults.Pages!.AccountSelector!.Subheading
                    },
                    Shared = new OidcUiSharedPage
                    {
                        FooterText = saved?.Pages?.Shared?.FooterText ?? defaults.Pages!.Shared!.FooterText
                    }
                }
            };
        }

        private static OidcUiThemePalette MergeThemePalette(
            OidcUiThemePalette? saved,
            OidcUiThemePalette defaults)
        {
            return new OidcUiThemePalette
            {
                Primary = saved?.Primary ?? defaults.Primary,
                Secondary = saved?.Secondary ?? defaults.Secondary,
                Background = saved?.Background ?? defaults.Background,
                Surface = saved?.Surface ?? defaults.Surface,
                Text = saved?.Text ?? defaults.Text,
                MutedText = saved?.MutedText ?? defaults.MutedText,
                Success = saved?.Success ?? defaults.Success,
                Danger = saved?.Danger ?? defaults.Danger,
                Border = saved?.Border ?? defaults.Border,
                BorderStrong = saved?.BorderStrong ?? defaults.BorderStrong,
                AccentSoft = saved?.AccentSoft ?? defaults.AccentSoft
            };
        }

        private static OidcUiThemePalette? CreateLegacyThemePalette(OidcUiTemplateTheme? legacy)
        {
            if (legacy is null || new[]
                {
                    legacy.Primary,
                    legacy.Secondary,
                    legacy.Background,
                    legacy.Surface,
                    legacy.Text,
                    legacy.MutedText,
                    legacy.Success,
                    legacy.Danger,
                    legacy.Border,
                    legacy.BorderStrong,
                    legacy.AccentSoft
                }.All(value => value is null))
            {
                return null;
            }

            return new OidcUiThemePalette
            {
                Primary = legacy.Primary,
                Secondary = legacy.Secondary,
                Background = legacy.Background,
                Surface = legacy.Surface,
                Text = legacy.Text,
                MutedText = legacy.MutedText,
                Success = legacy.Success,
                Danger = legacy.Danger,
                Border = legacy.Border,
                BorderStrong = legacy.BorderStrong,
                AccentSoft = legacy.AccentSoft
            };
        }

        public async Task<IActionResult> StartAuthenticationFlowAsync(string clientId, string redirectUri, string? forwardedTo)
        {
            try
            {
                var effectiveTenantId = BlocksContext.GetContext()?.TenantId;

                // One ClientId maps to one provider entry.
                var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(clientId);
                if (identityProvider == null || !identityProvider.IsActive)
                {
                    return new BadRequestObjectResult(new { error = "invalid_client", error_description = "ClientId not found or inactive" });
                }

                if (string.IsNullOrWhiteSpace(redirectUri))
                {
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirectUri is required" });
                }

                var allowedRedirectUris = identityProvider.RedirectUris ?? [];
                if (!allowedRedirectUris.Any(x => string.Equals(x, redirectUri, StringComparison.OrdinalIgnoreCase)))
                {
                    return new BadRequestObjectResult(new { error = "invalid_redirect_uri", error_description = "redirectUri is not registered for this client" });
                }

                // Generate OIDC flow parameters
                var state = GenerateRandomBase64Url(16);
                var nonce = GenerateRandomBase64Url(16);
                var codeVerifier = identityProvider.RequirePkce ? GenerateRandomBase64Url(32) : null;
                var codeChallenge = codeVerifier != null ? GenerateCodeChallenge(codeVerifier) : null;

                // Store flow context in cache (10 minute TTL)
                var flowContext = new
                {
                    state,
                    nonce,
                    codeVerifier,
                    provider = identityProvider.Provider,
                    tenantId = effectiveTenantId,
                    clientId = identityProvider.ClientId,
                    redirectUri,
                    createdAt = DateTime.UtcNow,
                    forwardedTo = forwardedTo
                };
                var cacheKey = $"idp_flow:{state}";
                await _cacheClient.AddStringValueAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(flowContext), IdpConstants.IdpFlowCacheTtlSeconds);

                // Build authorization URL
                var authorizeUrl = BuildAuthorizeUrl(identityProvider, redirectUri, state, nonce, codeChallenge);

                _logger.LogInformation("Started authentication flow for provider {Provider} with state {State}", identityProvider.Provider, state);

                // Return authorize URL - Frontend will redirect to IdP
                return new OkObjectResult(new { redirect_uri = authorizeUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting authentication flow");
                return new ObjectResult(new { error = "server_error", error_description = "Failed to start authentication flow" })
                {
                    StatusCode = 500
                };
            }
        }

        public async Task<IActionResult> HandleCallbackAsync(string? code, string? state, string? error, string? error_description, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            try
            {
                var (validation, flowContext, identityProvider, cacheKey) = await ValidateCallbackRequestAsync(code, state, error, error_description);
                if (validation != null)
                {
                    return validation;
                }

                var (exchangeError, tokenResponse) = await ExchangeCodeWithIdPAsync(identityProvider!, code!, flowContext!, httpRequest);
                if (exchangeError != null)
                {
                    return exchangeError;
                }

                var authCode = await _authCodeRepo.GetByCodeAsync(code!);

                if (authCode.Impersonated)
                {
                    var impersonatedResult = await _authenticationFlowService.ExecuteImpersonateAsync(
                        new ImpersonateRequest
                        {
                            TargetTenantId = authCode.TargetedTenantId,
                            ImpersontingUserId = authCode.ImpersonatedUserId,
                            RefreshToken = tokenResponse!.RefreshToken,
                            OrganizationId = authCode.OrganizationId,
                        },
                        httpRequest,
                        httpResponse
                    );

                    await _cacheClient.RemoveKeyAsync(cacheKey);
                    return new OkObjectResult(new { Impersonated = true });
                }

                // Resolve tenant_id: flowContext > BlocksContext > default
                var blocksContext = BlocksContext.GetContext();
                var resolvedTenantId = flowContext!.TenantId ?? blocksContext?.TenantId ?? string.Empty;
                var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
                var configuredAccessLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * IdpConstants.SecondsPerMinute, IdpConstants.MinAccessTokenLifetimeSeconds);
                // One resolver decides the lifetimes for cookie, Redis TTL and MongoDB alike, so no path
                // can disagree with another.
                var (_, resolvedAbsoluteMinutes) = RefreshTokenLifetimeResolver.Resolve(authConfiguration, _logger);
                var configuredRefreshLifetimeMinutes = Math.Max(resolvedAbsoluteMinutes, IdpConstants.MinTokenLifetimeMinutes);
                var resolvedAccessLifetimeSeconds = tokenResponse!.ExpiresIn.HasValue
                    ? Math.Max(tokenResponse.ExpiresIn.Value, IdpConstants.MinAccessTokenLifetimeSeconds)
                    : configuredAccessLifetimeSeconds;

                var tenant = _tenants.GetTenantByID(resolvedTenantId);
                var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
                var tokenResponseObj = new TokenResponse
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    IdToken = tokenResponse.IdToken,
                    TokenType = tokenResponse.TokenType ?? "Bearer",
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(resolvedAccessLifetimeSeconds),
                    RefreshExpiresUtc = DateTime.UtcNow.AddMinutes(configuredRefreshLifetimeMinutes),
                    Scope = tokenResponse.Scope,
                    CookieDomain = cookieDomain
                };

                await _cacheClient.RemoveKeyAsync(cacheKey);

                if (isResolved && !string.IsNullOrWhiteSpace(domain))
                {
                    AppendCookies(tokenResponseObj, httpResponse, domain);
                    _logger.LogInformation("Successfully completed authentication flow for state: {State}", state);

                    return new OkObjectResult(new
                    {
                        id_token = tokenResponseObj.IdToken,
                        token_type = tokenResponseObj.TokenType ?? "Bearer",
                        expires_in = (tokenResponseObj.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                        scope = tokenResponseObj.Scope
                    });
                }


                _logger.LogInformation("Successfully completed authentication flow for state: {State}", state);

                return new OkObjectResult(new
                {
                    access_token = tokenResponseObj.AccessToken,
                    refresh_token = tokenResponseObj.RefreshToken,
                    id_token = tokenResponseObj.IdToken,
                    token_type = tokenResponseObj.TokenType ?? "Bearer",
                    expires_in = (tokenResponseObj.ExpiresUtc - DateTime.UtcNow).TotalSeconds,
                    scope = tokenResponseObj.Scope
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IdP callback");
                return new ObjectResult(new { error = "server_error", error_description = "Authentication failed" })
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<(IActionResult? Error, FlowContext? FlowContext, IdentityProvider? IdentityProvider, string CacheKey)> ValidateCallbackRequestAsync(
            string? code,
            string? state,
            string? error,
            string? error_description)
        {
            // Check for IdP errors
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning("IdP returned error: {Error}, description: {ErrorDescription}", error, error_description);
                return (new BadRequestObjectResult(new
                {
                    error = error,
                    error_description = error_description ?? "Authorization failed at provider"
                }), null, null, string.Empty);
            }

            // Validate authorization code and state
            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogWarning("Callback received without authorization code");
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "code is required" }), null, null, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                _logger.LogWarning("Callback received without state parameter");
                return (new BadRequestObjectResult(new { error = "invalid_request", error_description = "state is required" }), null, null, string.Empty);
            }

            // Retrieve flow context from cache
            var cacheKey = $"idp_flow:{state}";
            var flowContextJson = await _cacheClient.GetStringValueAsync(cacheKey);

            if (string.IsNullOrWhiteSpace(flowContextJson))
            {
                _logger.LogWarning("Flow context not found or expired for state: {State}", state);
                return (new BadRequestObjectResult(new { error = "invalid_state", error_description = "State not found or expired (5 minute timeout)" }), null, null, string.Empty);
            }

            // Deserialize flow context
            var flowContext = System.Text.Json.JsonSerializer.Deserialize<FlowContext>(flowContextJson);
            if (flowContext == null)
            {
                _logger.LogWarning("Failed to deserialize flow context for state: {State}", state);
                return (new BadRequestObjectResult(new { error = "server_error", error_description = "Invalid flow context" }), null, null, cacheKey);
            }

            if (string.IsNullOrWhiteSpace(flowContext.Provider))
            {
                _logger.LogWarning("Flow context missing provider for state: {State}", state);
                return (new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider missing in flow context" }), null, null, cacheKey);
            }

            // Get IdP config
            var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(flowContext.Provider);
            if (identityProvider == null || !identityProvider.IsActive)
            {
                _logger.LogWarning("Identity provider not found or inactive: {Provider}", flowContext.Provider);
                return (new BadRequestObjectResult(new { error = "invalid_provider", error_description = "Provider not configured" }), null, null, cacheKey);
            }

            return (null, flowContext, identityProvider, cacheKey);
        }

        private async Task<(IActionResult? Error, OidcTokenEndpointResponse? TokenResponse)> ExchangeCodeWithIdPAsync(
            IdentityProvider identityProvider,
            string code,
            FlowContext flowContext,
            HttpRequest httpRequest)
        {
            var form = new Dictionary<string, string>
            {
                { "grant_type", GrantTypes.AuthCode },
                { "code", code },
                { "client_id", identityProvider.ClientId ?? string.Empty },
                { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                { "redirect_uri", flowContext.RedirectUri ?? string.Empty }
            };

            // Add PKCE code_verifier if present
            if (!string.IsNullOrWhiteSpace(flowContext.CodeVerifier))
            {
                form["code_verifier"] = flowContext.CodeVerifier;
            }

            try
            {
                var timeoutSeconds = (int)GetOutboundRequestTimeout().TotalSeconds;
                var (tokenResponse, tokenError) = await ExchangeCodeForTokenAsync(
                    identityProvider.TokenUrl,
                    form,
                    httpRequest.HttpContext.RequestAborted,
                    timeoutSeconds: timeoutSeconds);

                if (!string.IsNullOrWhiteSpace(tokenError) || tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    var detail = !string.IsNullOrWhiteSpace(tokenError)
                        ? tokenError
                        : (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                            ? "empty or invalid token response from IdP"
                            : "unknown";
                    _logger.LogWarning("Token exchange failed: {TokenError}", detail);
                    return (new BadRequestObjectResult(new { error = "invalid_grant", error_description = $"Failed to exchange authorization code: {detail}" }), null);
                }

                return (null, tokenResponse);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token exchange request failed");
                return (new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Failed to exchange authorization code" }), null);
            }
        }

        private string GenerateRandomBase64Url(int byteLength)
        {
            var randomBytes = new byte[byteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Base64UrlEncode(randomBytes);
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            var verifierBytes = Encoding.UTF8.GetBytes(codeVerifier);
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var digestBytes = sha256.ComputeHash(verifierBytes);
                return Base64UrlEncode(digestBytes);
            }
        }

        private string Base64UrlEncode(byte[] data)
        {
            var base64 = Convert.ToBase64String(data);
            return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            return CookieHelper.AppendCookies(response, httpResponse, domain);
        }

        private static void DeleteCookie(HttpResponse httpResponse, string domain, CookieOptions accessCookieOptions, CookieOptions refreshCookieOptions)
        {
            CookieHelper.DeleteAccessAndRefreshTokenCookies(httpResponse, domain, accessCookieOptions, refreshCookieOptions);
        }

        private static TimeSpan GetOutboundRequestTimeout()
        {
            return DomainResolver.IsLocalhost()
                ? TimeSpan.FromMinutes(IdpConstants.OutboundRequestLocalhostTimeoutMinutes)
                : TimeSpan.FromSeconds(IdpConstants.BackchannelTimeoutSeconds);
        }

        private async Task<(OidcTokenEndpointResponse? Response, string Error)> ExchangeCodeForTokenAsync(
            string tokenEndpoint,
            Dictionary<string, string> form,
            CancellationToken cancellationToken,
            int? timeoutSeconds = null)
        {
            return await _idpTokenExchangeClient.ExchangeCodeForTokenAsync(tokenEndpoint, form, cancellationToken, timeoutSeconds);
        }

        private string BuildAuthorizeUrl(IdentityProvider provider, string redirectUri, string state, string nonce, string? codeChallenge)
        {
            var queryParams = new Dictionary<string, string>
            {
                { "client_id", provider.ClientId ?? string.Empty },
                { "response_type", provider.ResponseType ?? "code" },
                { "redirect_uri", redirectUri },
                { "scope", provider.Scope ?? IdpConstants.OpenIdProfileEmailScope },
                { "state", state },
                { "nonce", nonce }
            };

            if (provider.RequirePkce && !string.IsNullOrEmpty(codeChallenge))
            {
                queryParams["code_challenge"] = codeChallenge;
                queryParams["code_challenge_method"] = IdpConstants.PkceMethodS256;
            }

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            var baseUrl = provider.AuthorizationUrl ?? string.Empty;
            var separator = baseUrl.EndsWith("?") || baseUrl.EndsWith("&")
                ? string.Empty
                : (baseUrl.Contains('?') ? "&" : "?");

            return $"{baseUrl}{separator}{queryString}";
        }

        private sealed class FlowContext
        {
            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("nonce")]
            public string? Nonce { get; set; }

            [JsonPropertyName("codeVerifier")]
            public string? CodeVerifier { get; set; }

            [JsonPropertyName("provider")]
            public string? Provider { get; set; }

            [JsonPropertyName("tenantId")]
            public string? TenantId { get; set; }

            [JsonPropertyName("clientId")]
            public string? ClientId { get; set; }

            [JsonPropertyName("redirectUri")]
            public string? RedirectUri { get; set; }

            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; }

            [JsonPropertyName("forwardedTo")]
            public string? ForwardedTo { get; set; } = null!;
        }
    }
}
