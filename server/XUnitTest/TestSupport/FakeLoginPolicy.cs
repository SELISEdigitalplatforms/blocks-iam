using Authentication.DomainService.Authentication.LoginPolicies;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.TestSupport
{
    /// <summary>A login policy that allows everything unless a test sets what it answers.</summary>
    public sealed class FakeLoginPolicy : ILoginPolicyService
    {
        public LoginPolicyDecision ChannelDecision { get; set; } = LoginPolicyDecision.Allowed;
        public Func<LoginChannel, string, OidcClientRegistration?, IdentityProvider?, LoginPolicyDecision> Decide { get; set; } =
            (_, _, _, _) => LoginPolicyDecision.Allowed;
        public Func<IdpSessionAccount, bool> SessionAccountAllowed { get; set; } = _ => true;
        public LoginOptions Options { get; set; } = new(true, false, []);
        public List<(LoginChannel Channel, string Method)> Checks { get; } = [];

        public Task<LoginPolicyDecision> CheckAsync(LoginChannel channel, string method, OidcClientRegistration? client = null, IdentityProvider? provider = null)
        {
            Checks.Add((channel, method));
            return Task.FromResult(Decide(channel, method, client, provider));
        }

        public Task<LoginOptions> GetOptionsAsync(LoginChannel channel, OidcClientRegistration? client) => Task.FromResult(Options);

        public Task<LoginPolicyDecision> CheckChannelAsync(LoginChannel channel) => Task.FromResult(ChannelDecision);

        public Task<Func<IdpSessionAccount, bool>> SessionAccountFilterAsync(OidcClientRegistration? client) =>
            Task.FromResult(SessionAccountAllowed);
    }
}
