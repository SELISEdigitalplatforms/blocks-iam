using Blocks.Genesis;
using Blocks.Genesis.DataAccess;

namespace Iam.DomainService.Services;

/// <summary>
/// The caller for the Genesis data access resolver: the BlocksContext set by the token. An impersonated token
/// bypasses data access policies (<see cref="AccessPrincipal.IsRootTenant"/>), as in the Data Gateway; every other
/// caller is held to the schema's policies. Validation rules and default values still run.
/// </summary>
public sealed class BlocksContextPrincipalAccessor : IAccessPrincipalAccessor
{
    public AccessPrincipal Current => For(BlocksContext.GetContext());

    /// <summary>The principal for a BlocksContext: policies are bypassed only for an impersonated token.</summary>
    public static AccessPrincipal For(BlocksContext? context) =>
        AccessPrincipal.From(context, isRootTenant: context?.Impersonated == true);
}
