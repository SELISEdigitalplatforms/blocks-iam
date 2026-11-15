using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// The JSON answer of an OIDC authorize refused for organization status (403 <c>organization_disabled</c>,
    /// 503 <c>temporarily_unavailable</c>; spec #668 §6.2). Its own type so <c>oidc/login</c> can tell a refused
    /// sign-in from a completed one and skip the success audit (spec #668 Q7).
    /// </summary>
    public sealed class OrganizationRefusalResult : ObjectResult
    {
        public OrganizationRefusalResult(string error, string errorDescription, int statusCode)
            : base(new { error, error_description = errorDescription })
        {
            StatusCode = statusCode;
        }
    }
}
