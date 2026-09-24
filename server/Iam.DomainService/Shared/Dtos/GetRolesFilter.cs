namespace Iam.DomainService.Dtos
{
    public class GetRolesFilter
    {
        /// <summary>
        /// Optional free-text match on name or description.
        /// <para>
        /// Nullable deliberately. Under &lt;Nullable&gt;enable&lt;/Nullable&gt; a non-nullable string
        /// on a bound model is implicitly [Required], so a caller filtering by slugs alone -- a
        /// legitimate request, and the only thing the bulk role dialog needs -- was rejected with
        /// "The Search field is required." The repository already treats blank as "no search".
        /// </para>
        /// </summary>
        public string? Search { get; set; }

        public List<string> Slugs { get; set; } = [];
    }
}
