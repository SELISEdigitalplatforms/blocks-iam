using Blocks.Genesis;
using Iam.DomainService.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Mfa.DomainService.Configuration
{
    [BsonIgnoreExtraElements]
    public class MfaConfiguration : BaseEntity
    {
        public string? Name { get; set; } = "Default";
        public bool EnableMfa { get; set; }
        public List<UserMfaType> UserMfaTypes { get; set; } = [];
        public MfaTemplate? MfaTemplate { get; set; } = new();
        public bool RequireMfaForAllUsers { get; set; }
        public List<string> MfaRequiredRoles { get; set; } = [];
        public List<string> MfaExemptRoles { get; set; } = [];
        public bool AllowUserOptOut { get; set; } = true;
        public bool AllowBackupCodes { get; set; } = true;
        public int BackupCodesCount { get; set; } = 10;
    }
}
