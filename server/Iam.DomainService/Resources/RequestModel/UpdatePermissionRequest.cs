namespace Iam.DomainService.Resources
{
    public class UpdatePermissionRequest : PermissionRequestBase
    {
        public string? ItemId { get; set; }
        public bool IsArchived { get; set; }
    }

}

