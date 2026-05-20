using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.MFA.Enums;

namespace CloudConfiguration.DomainService.MFA.RequestModel
{
    public class SaveMfaConfigurationRequest
    {
        public bool EnableMfa { get; set; }
        public List<CloudConfigurationUserMfaType> UserMfaType { get; set; }
        public MfaTemplate? MfaTemplate { get; set; }
    }

}
