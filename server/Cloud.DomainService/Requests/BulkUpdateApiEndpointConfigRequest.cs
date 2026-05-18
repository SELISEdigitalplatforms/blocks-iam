namespace Cloud.DomainService.Requests
{
    public class BulkUpdateApiEndpointConfigRequest
    {
        public List<string> ItemIds { get; set; } = [];
        public bool IsCaptchaRequired { get; set; }
        public bool IsMfaRequired { get; set; }
        public bool DisableAll { get; set; }
    }
}
