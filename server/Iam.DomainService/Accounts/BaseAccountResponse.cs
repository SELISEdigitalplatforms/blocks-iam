namespace Iam.DomainService.Accounts
{
    public class BaseAccountResponse
    {
        /// <summary>The user id. Not the organization id — see <see cref="OrganizationId"/>.</summary>
        public string? ItemId { get; set; }

        public bool IsSuccess { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Set when signup created an organization, so the caller does not have to look it up
        /// by name afterwards. Null on every other flow.
        /// </summary>
        public string? OrganizationId { get; set; }

        /// <summary>
        /// Free alternatives offered alongside a <c>name_already_exists</c> failure, so a caller
        /// can present a one-click fix rather than sending the user back to re-type a name.
        /// Empty on success and on every other failure.
        /// </summary>
        public List<string> OrganizationNameSuggestions { get; set; } = new List<string>();
    }
}
