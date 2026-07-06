namespace Authentication.DomainService.Shared.RequestModel
{
    public sealed class SaveClientCredentialRequest
    {
        public string? Name { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Permissions { get; set; } = new List<string>();
    }

    public sealed class DeleteClientCredentialRequest
    {
        public string? ItemId { get; set; }
    }
}
