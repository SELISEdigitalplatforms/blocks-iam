namespace Authentication.DomainService.Shared.RequestModel
{
    public sealed class GetAllClientCredentialsRequest
    {
        public string? ItemId { get; set; }
        public string? Name { get; set; }
    }
}
