namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Serializes tests that mutate the static <c>DomainResolver</c> HttpContextAccessor,
    /// so they never race against each other or against CookieHelper tests that read it.
    /// </summary>
    [CollectionDefinition("DomainResolverStatic")]
    public sealed class DomainResolverStaticCollection { }
}
