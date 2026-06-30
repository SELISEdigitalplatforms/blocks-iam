namespace Authentication.DomainService.RequestModel
{
    public sealed class UpdateSsoCredentialStatusRequest
    {
        public string? ItemId { get; set; }
        public bool IsEnabled { get; set; }
    }
}
