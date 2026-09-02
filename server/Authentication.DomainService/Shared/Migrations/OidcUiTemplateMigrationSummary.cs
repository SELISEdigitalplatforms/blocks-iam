namespace Authentication.DomainService.Migrations
{
    /// <summary>Operational outcome of one legacy-branding migration pass.</summary>
    public sealed class OidcUiTemplateMigrationSummary
    {
        public int TenantsExamined { get; internal set; }
        public int TenantsMigrated { get; internal set; }
        public int TenantsSkipped { get; internal set; }
        public int TenantsFailed { get; internal set; }
    }
}
