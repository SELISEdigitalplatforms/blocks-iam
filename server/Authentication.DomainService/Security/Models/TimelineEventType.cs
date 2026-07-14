namespace Authentication.DomainService.Security.Models
{
    /// <summary>
    /// Categories of events that can be appended to a security timeline for a
    /// session, token, or user. The value drives both audit reporting and the
    /// UI grouping shown on session/timeline views.
    /// </summary>
    public enum TimelineEventType
    {
        /// <summary>An authentication attempt succeeded and a new token was issued.</summary>
        Auth = 0,

        /// <summary>An existing token was refreshed (token rotation / sliding session).</summary>
        Refresh = 1,

        /// <summary>An active session or token was explicitly revoked by the user or system.</summary>
        Revocation = 2,
    }
}