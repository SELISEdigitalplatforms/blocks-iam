namespace Authentication.DomainService.Security.Models
{
    public enum SessionStatus
    {
        Active = 0,
        Expired = 1,
        Revoked = 2,
    }
}
