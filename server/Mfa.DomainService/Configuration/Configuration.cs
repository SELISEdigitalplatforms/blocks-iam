using Iam.DomainService.Entities;

namespace Mfa.DomainService.Configuration
{
    public class Configuration
    {
        public bool EnableMfa { get; set; }
        public List<UserMfaType> UserMfaType { get; set; }
        public MfaTemplate? MfaTemplate { get; set; }
    }

    public class MfaTemplate
    {
        public string TemplateName { get; set; }
        public string TemplateId { get; set; }
    }
}
