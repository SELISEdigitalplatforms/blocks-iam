using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Moq;

namespace XUnitTest.DomainService.OAuth;

public class AuthorizationClaimsResolverTests
{
    private readonly Mock<IUserRepository> _userRepository;
    private readonly Mock<IAuthenticationRepository> _authenticationRepository;
    private readonly AuthorizationClaimsResolver _resolver;

    public AuthorizationClaimsResolverTests()
    {
        _userRepository = new Mock<IUserRepository>();
        _authenticationRepository = new Mock<IAuthenticationRepository>();
        _resolver = new AuthorizationClaimsResolver(_userRepository.Object, _authenticationRepository.Object);
    }

    [Fact]
    public async Task ResolveAsync_FiltersPermissionsByRequestedScopeAndClientScopes()
    {
        var user = CreateUser();
        _userRepository
            .Setup(x => x.GetPermissionsByResourcesAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(
            [
                new GetUserPermission { Resource = "ai.predict" },
                new GetUserPermission { Resource = "ai.read" },
                new GetUserPermission { Resource = "os.manage" }
            ]);

        _userRepository
            .Setup(x => x.GetPermissionsByRolesAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(
            [
                new GetUserPermission { Resource = "ai.train" },
                new GetUserPermission { Resource = "billing.view" }
            ]);

        var result = await _resolver.ResolveAsync(
            user,
            "org-1",
            "openid profile ai",
            ["ai", "billing.view"],
            requireExplicitScope: true);

        Assert.Equal(["admin"], result.Roles);
        Assert.Equal(["ai.predict", "ai.read", "ai.train"], result.Permissions);
        Assert.Equal(["ai"], result.Resources);
    }

    [Fact]
    public async Task ResolveAsync_RequiresExplicitCustomScope_WhenConfigured()
    {
        var user = CreateUser();
        _userRepository
            .Setup(x => x.GetPermissionsByResourcesAsync(It.IsAny<List<string>>()))
            .ReturnsAsync([new GetUserPermission { Resource = "ai.predict" }]);
        _userRepository
            .Setup(x => x.GetPermissionsByRolesAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<GetUserPermission>());

        var result = await _resolver.ResolveAsync(user, "org-1", "openid profile email", ["ai"], requireExplicitScope: true);

        Assert.Empty(result.Permissions);
        Assert.Empty(result.Resources);
    }

    private static User CreateUser() => new()
    {
        ItemId = "user-1",
        Memberships =
        [
            new OrganizationMembership
            {
                OrganizationId = "org-1",
                Roles = ["admin"],
                Permissions = ["ai.predict", "ai.read", "os.manage", "legacypermission"]
            }
        ]
    };
}