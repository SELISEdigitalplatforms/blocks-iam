using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.SignupLinks;

public interface ISignupLinkEmbeddedTokenIssuer
{
    /// <summary>
    /// Issues tokens for a user an embedded-mode link has just proven, and returns the same
    /// body <c>POST auth/login</c> returns.
    /// </summary>
    Task<IActionResult> IssueAsync(SignupLink link, User user, List<string> amr, HttpRequest request);
}

/// <summary>
/// The terminal step of an embedded-mode redemption.
/// <para>
/// OIDC redemption ends in an authorize URL the browser follows, which is safe because
/// <c>redirect_uri</c> must be registered on the client. Embedded mode has no client
/// registration, so it does not redirect at all: it mints tokens here and hands them back to
/// the construct that called <c>/redeem</c>.
/// </para>
/// <para>
/// Tokens come from <see cref="IOAuthJwtAccessTokenManager"/>, the same component every other
/// grant ends at, so the restriction logic already written for the OIDC path
/// (<c>IsLinkAuthentication</c> role intersection, <c>amr</c>) applies unchanged and refresh
/// keeps the restrictions. No new grant is exposed on the public token endpoint.
/// </para>
/// </summary>
public sealed class SignupLinkEmbeddedTokenIssuer : ISignupLinkEmbeddedTokenIssuer
{
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IOAuthJwtAccessTokenManager _tokenManager;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<SignupLinkEmbeddedTokenIssuer> _logger;

    public SignupLinkEmbeddedTokenIssuer(
        IAuthenticationRepository authenticationRepository,
        IOAuthJwtAccessTokenManager tokenManager,
        IAuthenticationService authenticationService,
        ILogger<SignupLinkEmbeddedTokenIssuer> logger)
    {
        _authenticationRepository = authenticationRepository;
        _tokenManager = tokenManager;
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public async Task<IActionResult> IssueAsync(
        SignupLink link,
        User user,
        List<string> amr,
        HttpRequest request)
    {
        var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (configuration == null)
        {
            _logger.LogWarning("Embedded signup-link redemption found no authentication configuration");
            return new ObjectResult(new { error = "server_error" }) { StatusCode = 500 };
        }

        var tokenRequest = new TokenRequest
        {
            GrantType = GrantTypes.SignupLink,
            OrganizationId = link.OrganizationId,
            Request = request,
            IsLinkAuthentication = true,
            RestrictedRoles = link.Roles?.ToList() ?? [],
            RestrictedPermissions = link.Permissions?.ToList() ?? [],
            Amr = amr

            // Audience is deliberately left null: an embedded link has no client to narrow to,
            // so the token carries the tenant audience. The restriction that remains is the
            // role and permission intersection, which is what bounds authority. Recorded in
            // SPEC26 A6 as a real difference from the OIDC path.
        };

        var tokenResponse = await _tokenManager.ManageTokenAsync(tokenRequest, configuration, user);

        // BuildFlowResultAsync owns cookie-versus-body placement and the MFA challenge shape,
        // so an embedded redemption answers in exactly the form the construct already parses
        // from POST auth/login.
        return await _authenticationService.BuildFlowResultAsync(
            new AuthenticationFlowResult { TokenResponse = tokenResponse },
            request.HttpContext);
    }
}
