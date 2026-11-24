namespace Iam.DomainService.Dtos
{
    public class SendMail
    {
        public Dictionary<string, string> SubjectDataContext { get; set; } = new Dictionary<string, string>();
        public IEnumerable<string> To { get; set; }
        public IEnumerable<string> Bcc { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Cc { get; set; } = Enumerable.Empty<string>();
        public string Purpose { get; set; }
        public string Language { get; set; }
        public IEnumerable<string> ReplyTo { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Attachments { get; set; } = Enumerable.Empty<string>();
        public Dictionary<string, string> BodyDataContext { get; set; } = new Dictionary<string, string>();
        public bool SendPhoneNumberAsEmail { get; set; } = false;

        /// <summary>
        /// The organization whose template the mail service picks (its own, else the default organization's).
        /// Null keeps the mail service's choice for queued mails, the default organization. Set by batch mails.
        /// </summary>
        public string? OrganizationId { get; set; }

        /// <summary>Echoed back by the mail service on its sent event, so a sender can match the outcome.</summary>
        public string? CorrelationId { get; set; }
    }
}
