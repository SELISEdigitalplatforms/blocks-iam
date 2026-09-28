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
}
