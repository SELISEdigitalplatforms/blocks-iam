namespace Authentication.DomainService.Authentication
{
    public class LogoutRequest
    {
        public string? RefreshToken { get; set; }
    }

    public class LogoutAllRequest
    {
        public bool UseBackchannel { get; set; } = false;
    }

    public class LogoutResponse
    {
        public bool IsSuccess { get; set; }
    }
}
