namespace Authentication.DomainService.Authentication
{
    public static class SessionAuditEvents
    {
        public const string SessionCreated = "session_created";
        public const string AccountAdded = "account_added";
        public const string AccountSelected = "account_selected";
        public const string AccountRemoved = "account_removed";
        public const string SessionRotated = "session_rotated";
        public const string SessionRevoked = "session_revoked";
        public const string UserRevokedSession = "user_revoked_session";
    }
}
