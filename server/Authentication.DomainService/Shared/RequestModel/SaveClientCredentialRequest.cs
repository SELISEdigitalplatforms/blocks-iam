namespace Authentication.DomainService.Shared.RequestModel
{
    public class SaveClientCredentialRequest
    {
        public string? Name { get; set; }
        public List<string> Roles { get; set; } = [];
        public Dictionary<string, List<string>> PermissionsByOrg { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class DeleteClientCredentialRequest
    {
        public string? ItemId { get; set; }
    }
}
