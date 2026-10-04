using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Read-only context for the join page. Never mutates SignupLink counters (C2).
/// </summary>
public sealed class SignupLinkContextService : ISignupLinkContextService
{
    private readonly ISignupLinkRepository _links;
    private readonly ISignupLinkConfigurationRepository _configurations;
    private readonly IOidcClientRegistrationLookup _oidc;
    private readonly ILogger<SignupLinkContextService> _logger;

    public SignupLinkContextService(
        ISignupLinkRepository links,
        ISignupLinkConfigurationRepository configurations,
        IOidcClientRegistrationLookup oidc,
        ILogger<SignupLinkContextService> logger)
    {
        _links = links;
        _configurations = configurations;
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
            _logger.LogDebug("Signup link context miss for supplied code hash");
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

        return new SignupLinkContextResponse
        {
            Valid = true,
            CredentialMode = link.CredentialMode.ToString(),
            FirstName = link.FirstName,
            MaskedEmail = SignupLinkCodeHasher.MaskEmail(link.Email),
            ApplicationName = await ResolveApplicationNameAsync(link),
            Mode = link.Mode.ToString()
        };
    }

    /// <summary>
    /// An embedded link has no OidcClientRegistration to read a name from, so it falls back to
    /// the configuration's own name -- which is what the invitee was invited under.
    /// </summary>
    private async Task<string?> ResolveApplicationNameAsync(SignupLink link)
    {
        if (link.Mode == SignupLinkMode.Embedded)
        {
            var config = await _configurations.GetByIdAsync(link.ConfigurationId, link.TenantId);
            return config?.Name;
        }

        var client = await _oidc.GetByClientIdAsync(link.ClientId);
        return !string.IsNullOrWhiteSpace(client?.ClientName)
            ? client!.ClientName!
            : link.ClientId;
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

        if (!SignupLink.HasRedemptionBudget(
                link.RedemptionCount, link.MaxRedemptions))
        {
            return false;
        }

        return true;
    }
}
