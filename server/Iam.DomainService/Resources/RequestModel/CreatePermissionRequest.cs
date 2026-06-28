namespace Iam.DomainService.Resources
{
    public class CreatePermissionRequest : PermissionRequestBase
    {
        public bool PropagateToOtherOrg { get; set; } = false;
    }
}
