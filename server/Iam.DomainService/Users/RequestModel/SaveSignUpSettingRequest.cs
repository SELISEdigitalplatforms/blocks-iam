namespace Iam.DomainService.Users.RequestModel
{
    public class SaveSignUpSettingRequest
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
        public List<string> DefaultRolesForNewUserOnSignUp { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForNewUserOnSignUp { get; set; } = new List<string>();
    }
}
