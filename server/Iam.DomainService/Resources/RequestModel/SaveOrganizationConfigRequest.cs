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

        /// <summary>
        /// Whether organization names must be unique within the tenant. Nullable, unlike its
        /// neighbours: every payload written before this field existed omits it, and a plain bool
        /// would read that absence as "turn it off" -- silently dropping a tenant's enforcement on
        /// the next unrelated config save. Null means "leave the stored value as it is".
        /// </summary>
        public bool? IsOrgNameUniquenessEnabled { get; set; }
    }

}
