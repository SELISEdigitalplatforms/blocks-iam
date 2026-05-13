namespace Iam.DomainService.Users.RequestModel
{
    public class SaveSignUpSettingRequest
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
    }
}
