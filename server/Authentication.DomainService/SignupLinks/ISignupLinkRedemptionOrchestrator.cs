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
    /// Runs after a successful activation, and only when the activation key's
    /// <c>UserKeyMap.Value</c> carries <c>signup-link:{linkId}</c>. A no-op for ordinary
    /// invites and recoveries, whose Value is a URL and can never carry that prefix.
    /// <para>
    /// Returns a non-null result when the link's configuration asked for
    /// <c>SignInAfterActivation</c>: the caller should return it in place of the plain
    /// activation response, because it carries the session (and, on an MFA account, the
    /// challenge). Returns null when the activation was left to sign in normally.
    /// </para>
    /// </summary>
    Task<IActionResult?> CompleteActivationAsync(
        string? activationCode,
        HttpRequest request,
        HttpResponse response);
}
