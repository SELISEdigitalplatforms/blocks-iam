using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Read-only context for the join page. Never mutates SignupLink counters (C2).
/// </summary>
public sealed class SignupLinkContextService : ISignupLinkContextService
{
    private readonly ISignupLinkRepository _links;
    private readonly IOidcClientRegistrationLookup _oidc;
    private readonly ILogger<SignupLinkContextService> _logger;

    public SignupLinkContextService(
        ISignupLinkRepository links,
        IOidcClientRegistrationLookup oidc,
        ILogger<SignupLinkContextService> logger)
    {
        _links = links;
        _oidc = oidc;
        _logger = logger;
    }

    public async Task<SignupLinkContextResponse> GetContextAsync(string? code, string? tenantIdHint)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return SignupLinkContextResponse.Invalid();
        }

        var link = await _links.GetByCodeHashAsync(SignupLinkCodeHasher.Hash(code));
        if (link == null)
        {
            return SignupLinkContextResponse.Invalid();
        }

        if (!string.IsNullOrWhiteSpace(tenantIdHint)
            && !string.Equals(link.TenantId, tenantIdHint, StringComparison.OrdinalIgnoreCase))
        {
            return SignupLinkContextResponse.Invalid();
        }

        if (!IsPresentable(link))
        {
            return SignupLinkContextResponse.Invalid();
        }

        var client = await _oidc.GetByClientIdAsync(link.ClientId);
        var applicationName = !string.IsNullOrWhiteSpace(client?.ClientName)
            ? client!.ClientName!
            : link.ClientId;

        return new SignupLinkContextResponse
        {
            Valid = true,
            CredentialMode = link.CredentialMode.ToString(),
            FirstName = link.FirstName,
            MaskedEmail = SignupLinkCodeHasher.MaskEmail(link.Email),
            ApplicationName = applicationName
        };
    }

    private static bool IsPresentable(SignupLink link)
    {
        if (link.Status != SignupLinkStatus.Active)
        {
            return false;
        }

        if (link.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return false;
        }

        if (link.RedemptionCount >= link.MaxRedemptions)
        {
            return false;
        }

        return true;
    }
}
