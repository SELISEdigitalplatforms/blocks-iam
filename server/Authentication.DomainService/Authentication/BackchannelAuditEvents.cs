namespace Authentication.DomainService.Authentication
{
    public static class BackchannelAuditEvents
    {
        public const string Dispatch = "dispatch_backchannel_logout";
        public const string Delivery = "backchannel_logout_delivery";
        public const string Delivered = "backchannel_logout_delivered";
        public const string DeliveryFailed = "backchannel_logout_delivery_failed";
        public const string Succeeded = "backchannel_logout_succeeded";
        public const string Failed = "backchannel_logout_failed";
        public const string Exception = "backchannel_logout_exception";
    }
}
