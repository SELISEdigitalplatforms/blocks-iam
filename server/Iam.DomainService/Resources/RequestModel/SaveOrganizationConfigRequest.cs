namespace Iam.DomainService.Resources
{
    public class SaveOrganizationConfigRequest
    {
        public bool AllowOrgCreationFromCloud { get; set; }
        public bool AllowOrgCreationFromConstruct { get; set; }
        public bool AllowOrgCreationFromSignup { get; set; }
        public bool AllowOrgCreationFromPortal { get; set; }
        public bool IsMultiOrgEnabled { get; set; }
        public bool ConsentForMultiOrgEnable { get; set; } 
        public DateTime ConsentTimeForMultiOrgEnable { get; set; }
        public List<string> DefaultRolesOnOrgCreation { get; set; } = new List<string>();
        public List<string> DefaultPermissionsOnOrgCreation { get; set; } = new List<string>();
        public bool KeepOrgRolesSameAsDefaultRoles { get; set; } = true;
        public bool KeepOrgPermissionsSameAsDefaultPermissions { get; set; } = true;
    }

}
