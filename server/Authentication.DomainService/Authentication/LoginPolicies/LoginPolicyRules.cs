using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Authentication.LoginPolicies
{
    /// <summary>Where a sign-in starts: the app's own login form, or the IAM login page.</summary>
    public enum LoginChannel
    {
        Embedded,
        Oidc
    }

    /// <summary>How a user signs in. Also the value kept on <see cref="IdpSessionAccount.LoginMethod"/>.</summary>
    public static class LoginMethods
    {
        public const string Password = "password";
        public const string Social = "social";
    }

    public sealed record LoginPolicyDecision(bool IsAllowed, string? Message)
    {
        public static readonly LoginPolicyDecision Allowed = new(true, null);

        public static LoginPolicyDecision Denied(string message) => new(false, message);
    }

    /// <summary>What a login page may offer: the password form, social buttons, and which providers.</summary>
    public sealed record LoginOptions(bool Password, bool Social, IReadOnlyList<IdentityProvider> Providers);

    /// <summary>
    /// The tenant login policy as pure rules. The tenant decides where (embedded, OIDC) and how
    /// (password, social); an OIDC client can only narrow that; a provider must be enabled, and for an
    /// OIDC client in "selected" mode also chosen. A missing policy allows everything.
    /// </summary>
    public static class LoginPolicyRules
    {
        public const string Error = "login_method_not_allowed";

        public static LoginPolicyDecision Check(
            TenantLoginPolicy? tenant,
            LoginChannel channel,
            string method,
            OidcClientLoginPolicy? client,
            IdentityProvider? provider)
        {
            var policy = tenant ?? new TenantLoginPolicy();
            var isSocial = method == LoginMethods.Social;

            var channelDecision = CheckChannel(policy, channel);
            if (!channelDecision.IsAllowed)
                return channelDecision;

            if (!(isSocial ? policy.AllowSocial : policy.AllowPassword))
                return LoginPolicyDecision.Denied(isSocial
                    ? "Social sign-in is turned off for this project."
                    : "Password sign-in is turned off for this project.");

            if (channel == LoginChannel.Oidc && !ClientAllowsMethod(client, method))
                return LoginPolicyDecision.Denied(isSocial
                    ? "Social sign-in is turned off for this application."
                    : "Password sign-in is turned off for this application.");

            if (isSocial && provider != null && !ProviderAllowed(channel, client, provider))
                return LoginPolicyDecision.Denied("This sign-in provider is not available for this application.");

            return LoginPolicyDecision.Allowed;
        }

        /// <summary>Only the place: embedded login form or IAM login page.</summary>
        public static LoginPolicyDecision CheckChannel(TenantLoginPolicy? tenant, LoginChannel channel)
        {
            var policy = tenant ?? new TenantLoginPolicy();

            if (channel == LoginChannel.Embedded && !policy.AllowEmbedded)
                return LoginPolicyDecision.Denied("Signing in from this application is turned off for this project.");

            if (channel == LoginChannel.Oidc && !policy.AllowOidc)
                return LoginPolicyDecision.Denied("Signing in through the login page is turned off for this project.");

            return LoginPolicyDecision.Allowed;
        }

        /// <summary>
        /// Whether an existing IdP session account may sign in to an OIDC client silently. An account
        /// with no recorded method (made before methods were recorded) is not blocked.
        /// </summary>
        public static bool AllowsSessionAccount(TenantLoginPolicy? tenant, OidcClientLoginPolicy? client, string? accountLoginMethod) =>
            string.IsNullOrWhiteSpace(accountLoginMethod)
            || Check(tenant, LoginChannel.Oidc, accountLoginMethod, client, null).IsAllowed;

        /// <summary>An unknown stored value is read as Default, so a bad value never locks anyone out.</summary>
        public static bool ClientAllowsMethod(OidcClientLoginPolicy? client, string method) => client?.Method switch
        {
            OidcClientLoginMethods.Password => method == LoginMethods.Password,
            OidcClientLoginMethods.Social => method == LoginMethods.Social,
            _ => true
        };

        public static bool ProviderAllowed(LoginChannel channel, OidcClientLoginPolicy? client, IdentityProvider provider)
        {
            if (!provider.IsActive)
                return false;

            if (channel == LoginChannel.Embedded || client?.Providers != OidcClientProviderModes.Selected)
                return true;

            return client.SelectedProviderIds.Contains(provider.ItemId, StringComparer.Ordinal);
        }

        public static LoginOptions Options(
            TenantLoginPolicy? tenant,
            LoginChannel channel,
            OidcClientLoginPolicy? client,
            IEnumerable<IdentityProvider> socialProviders)
        {
            var password = Check(tenant, channel, LoginMethods.Password, client, null).IsAllowed;
            var providers = Check(tenant, channel, LoginMethods.Social, client, null).IsAllowed
                ? socialProviders.Where(p => ProviderAllowed(channel, client, p)).ToList()
                : [];

            return new LoginOptions(password, providers.Count > 0, providers);
        }
    }
}
