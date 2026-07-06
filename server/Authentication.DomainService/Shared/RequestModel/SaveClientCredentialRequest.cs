namespace Authentication.DomainService.Shared.RequestModel
{
    public sealed class SaveClientCredentialRequest
    {
        public string? Name { get; set; }
        public List<string> Roles { get; set; } = [];
        public Dictionary<string, List<string>> PermissionsByOrg { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DeleteClientCredentialRequest
    {
        public string? ItemId { get; set; }
    }
}
