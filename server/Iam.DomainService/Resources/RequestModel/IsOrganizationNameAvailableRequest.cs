namespace Iam.DomainService.Resources.RequestModel
{
    public class IsOrganizationNameAvailableRequest
    {
        public string? Name { get; set; }
    }

    public class IsOrganizationNameAvailableResponse
    {
        /// <summary>
        /// False when the question could not be answered — currently only when the tenant has
        /// multi-organization mode off, in which case <see cref="Errors"/> explains why.
        /// </summary>
        public bool IsSuccess { get; set; } = true;

        public bool IsAvailable { get; set; }

        /// <summary>
        /// Free alternatives, populated only when the requested name is taken. May be empty if
        /// none could be found.
        /// </summary>
        public List<string> Suggestions { get; set; } = new List<string>();

        /// <summary>
        /// Same error keys the organization mutation path uses, so a caller can handle
        /// <c>multi_org_disabled</c> identically wherever it surfaces.
        /// </summary>
        public Dictionary<string, string>? Errors { get; set; }
    }
}
