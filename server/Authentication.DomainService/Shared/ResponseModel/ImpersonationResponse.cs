namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>
    /// Response for starting impersonation.
    /// Tokens are sent only via HttpOnly cookies, not in response body.
    /// </summary>
    public class ImpersonateResponse
    {
        public bool ImpersonationMode { get; set; } = true;
    }

    /// <summary>
    /// Response for stopping impersonation and restoring root session.
    /// Tokens are sent only via HttpOnly cookies, not in response body.
    /// </summary>
    public class StopImpersonationResponse
    {
        public bool ImpersonationMode { get; set; } = false;
    }
}
