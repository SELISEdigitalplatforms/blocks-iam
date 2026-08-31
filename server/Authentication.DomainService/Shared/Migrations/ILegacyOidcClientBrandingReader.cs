namespace Authentication.DomainService.Migrations
{
    /// <summary>Reads the retired fields directly from their original MongoDB documents.</summary>
    public interface ILegacyOidcClientBrandingReader
    {
        Task<IReadOnlyList<LegacyOidcClientBranding>> ReadAsync(
            string databaseName,
            string connectionString,
            CancellationToken cancellationToken = default);
    }
}
