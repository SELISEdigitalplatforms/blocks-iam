using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.Services
{
    public interface IOtpServiceFactory
    {
        IOtpService GetOTPService(UserMfaType authType);
    }
}
