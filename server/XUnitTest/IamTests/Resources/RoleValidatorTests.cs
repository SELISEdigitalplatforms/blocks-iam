using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    public class RoleValidatorTests : IDisposable
    {
        private readonly Mock<IResourceRepository> _repo = new();

        public RoleValidatorTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            // By default the slug is unique.
            _repo.Setup(r => r.GetRoleBySlugAndOrgAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((Role)null!);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private RoleValidator Create() => new(_repo.Object);

        private static CreateRoleRequest Req(string name = "Admin", string slug = "admin") =>
            new() { Name = name, Slug = slug, Description = "d" };

        [Fact]
        public async Task ValidRequest_UniqueSlug_Passes()
        {
            var result = await Create().ValidateAsync(Req());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyName_Fails()
        {
            var result = await Create().ValidateAsync(Req(name: ""));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Name");
        }

        [Fact]
        public async Task NameTooLong_Fails()
        {
            var result = await Create().ValidateAsync(Req(name: new string('x', 151)));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Maximum_Character_Limit_150");
        }

        [Fact]
        public async Task EmptySlug_Fails()
        {
            var result = await Create().ValidateAsync(Req(slug: ""));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Slug");
        }

        [Fact]
        public async Task SlugWithSpaces_Fails()
        {
            var result = await Create().ValidateAsync(Req(slug: "has space"));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource name must not contain spaces");
        }

        [Fact]
        public async Task SlugTooLong_Fails()
        {
            var result = await Create().ValidateAsync(Req(slug: new string('a', 201)));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource name maximum character limit 200");
        }

        [Fact]
        public async Task DuplicateSlug_Fails()
        {
            _repo.Setup(r => r.GetRoleBySlugAndOrgAsync("admin", "default"))
                .ReturnsAsync(new Role { ItemId = "existing", Slug = "admin", Name = "A" });

            var result = await Create().ValidateAsync(Req());

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Role slug must be unique");
        }
    }
}
