namespace Iam.DomainService.DelegatedAdministration
{
    /// <summary>How the delegated-administration rules act on a request they would refuse.</summary>
    public enum DelegatedAdministrationMode
    {
        /// <summary>Compute every rule, log a structured warning on a would-deny, and allow the request.</summary>
        Audit,

        /// <summary>Refuse a would-deny with 403 and the generic <c>Access_Grant_Not_Allowed</c> error.</summary>
        Enforce
    }

    /// <summary>
    /// The <c>DelegatedAdministration</c> configuration section. One setting per service, and
    /// <see cref="DelegatedAdministrationMode.Audit"/> when it is absent, so a deploy that has not
    /// been configured yet refuses nothing it did not refuse before.
    /// </summary>
    public sealed class DelegatedAdministrationOptions
    {
        public const string SectionName = "DelegatedAdministration";

        public DelegatedAdministrationMode Mode { get; set; } = DelegatedAdministrationMode.Audit;
    }
}
