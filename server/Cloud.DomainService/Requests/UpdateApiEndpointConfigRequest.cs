namespace Cloud.DomainService.Requests
{
    public class UpdateApiEndpointConfigRequest
    {
        public string ItemId { get; set; } = string.Empty;
        public bool IsCaptchaRequired { get; set; }
        public bool IsMfaRequired { get; set; }
    }
}
