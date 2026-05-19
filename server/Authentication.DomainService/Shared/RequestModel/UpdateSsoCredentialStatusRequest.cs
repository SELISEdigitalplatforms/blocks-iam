namespace Authentication.DomainService.RequestModel
{
    public class UpdateSsoCredentialStatusRequest
    {
        public string? ItemId { get; set; }
        public bool IsEnabled { get; set; }
    }
}
