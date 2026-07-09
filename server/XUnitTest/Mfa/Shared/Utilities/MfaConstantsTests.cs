using FluentAssertions;
using Iam.DomainService.Utilities;

namespace XUnitTest.Mfa.Shared.Utilities
{
    public class IdpConstantsTests
    {
        [Fact]
        public void MfaConstants_HaveExpectedValues()
        {
            IdpConstants.MfaApiServiceName.Should().Be("blocks-mfa-api");
            IdpConstants.MfaWorkerServiceName.Should().Be("blocks-mfa-worker");
            IdpConstants.DefaultMfaTemplateName.Should().Be("MfaViaEmail");
            IdpConstants.DefaultMfaTemplateId.Should().Be("0b121378-3c3d-44f3-a855-9da08cbef48c");
            IdpConstants.MfaQueueName.Should().Be("blocks_mfa_listener");
        }
    }
}