using Authentication.DomainService.Shared;

using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Reads IdP session-timeout durations from environment variables with safe defaults.
    ///
    /// Two existing call sites use slightly different env-var names and units for the
    /// absolute timeout (HOURS vs DAYS). Both behaviours are preserved here as
    /// separate methods so callers can be migrated independently and audited
    /// before a final consolidation.
    /// </summary>
    public static class SessionTimeoutConfig
    {
        public static TimeSpan GetIdleTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= IdpConstants.MaxIdpSessionHours)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(IdpConstants.DefaultIdpSessionIdleHours);
        }

        public static TimeSpan GetAbsoluteTimeoutHours()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= IdpConstants.MaxIdpSessionHours)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(IdpConstants.DefaultIdpSessionAbsoluteHours);
        }

        public static TimeSpan GetAbsoluteTimeoutDays()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS");
            if (double.TryParse(configured, out var days) && days > 0 && days <= 365)
            {
                return TimeSpan.FromDays(days);
            }

            return TimeSpan.FromDays(30);
        }
    }
}
