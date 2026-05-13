using Blocks.Genesis;

namespace Identifier.DomainService.People
{
    public class ConfirmInvitationRequest
    {
        public string Code { get; set; }
    }

    public class ConfirmInvitationResponse : BaseMutationResponse
    {
        public string ActivationKey { get; set; }
    }
}
