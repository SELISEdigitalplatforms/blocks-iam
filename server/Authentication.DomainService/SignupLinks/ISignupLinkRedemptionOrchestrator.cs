using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.SignupLinks;

public interface ISignupLinkRedemptionOrchestrator
{
    Task<IActionResult> RedeemAsync(
        string? code,
        string? tenantIdHint,
        HttpRequest request,
        HttpResponse response);

    Task<IActionResult> CompleteRedeemMfaAsync(
        string? mfaId,
        string? mfaCode,
        HttpRequest request,
        HttpResponse response);

    /// <summary>
    /// After a successful <c>ProcessActivationAsync</c>, bind the link-session cookie when the
    /// activation key's UserKeyMap.Value carries <c>signup-link:{linkId}</c>. No-op for ordinary invites.
    /// </summary>
    Task TryBindLinkSessionAfterActivationAsync(
        string? activationCode,
        HttpRequest request,
        HttpResponse response);
}
