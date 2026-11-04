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
    /// The password step an already-active user takes when redeem answered
    /// <c>authentication_required</c>. On success it returns what redeem would have returned
    /// for the link's mode, or an MFA challenge when the account has one.
    /// </summary>
    Task<IActionResult> AuthenticateAsync(
        string? redemptionId,
        string? password,
        string? captchaCode,
        string? tenantIdHint,
        HttpRequest request,
        HttpResponse response);

    /// <summary>
    /// Runs after a successful activation, and only when the activation key's
    /// <c>UserKeyMap.Value</c> carries <c>signup-link:{linkId}</c>. A no-op for ordinary
    /// invites and recoveries, whose Value is a URL and can never carry that prefix.
    /// <para>
    /// A non-null result replaces the plain activation response. An OIDC link always gets one:
    /// the <c>authorizeUrl</c> back into its client, carrying a link session when the
    /// configuration asked for <c>SignInAfterActivation</c> and <c>prompt=login</c> otherwise.
    /// An embedded link gets one only with <c>SignInAfterActivation</c>: the session (or, on an
    /// MFA account, the challenge). Null leaves the activation to sign in normally.
    /// </para>
    /// </summary>
    Task<IActionResult?> CompleteActivationAsync(
        string? activationCode,
        HttpRequest request,
        HttpResponse response);

    /// <summary>
    /// Ends the signup-link session on logout when the request carries its cookie
    /// (<c>blocks-link-session-{tenantId}</c>, tenant of the signed-in user): deletes the session and
    /// clears the cookie. Without the cookie (e.g. a cross-site call) nothing happens and the session
    /// ends by its TTL.
    /// </summary>
    Task EndLinkSessionAsync(HttpRequest request, HttpResponse response);
}
