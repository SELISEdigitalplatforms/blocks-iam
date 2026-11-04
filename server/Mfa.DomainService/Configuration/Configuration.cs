using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.Configuration
{
    public class Configuration
    {
        public bool EnableMfa { get; set; }
        public List<UserMfaType> UserMfaType { get; set; } = [];
        public MfaTemplate? MfaTemplate { get; set; }
        public bool RequireMfaForAllUsers { get; set; }
        public List<string> MfaRequiredRoles { get; set; } = [];
        public List<string> MfaExemptRoles { get; set; } = [];
        public bool AllowUserOptOut { get; set; } = true;
        public bool AllowBackupCodes { get; set; } = true;
        public int BackupCodesCount { get; set; } = 10;
    }
}
