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
        public List<string> DefaultRoleOnOrgCreation { get; set; } = new List<string>();
        public List<string> DefaultPermissionOnOrgCreation { get; set; } = new List<string>();
    }

}
