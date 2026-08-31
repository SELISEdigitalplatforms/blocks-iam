using FluentAssertions;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    public class OrganizationNameResolverTests
    {
        private readonly Mock<IResourceRepository> _repo = new();

        private OrganizationNameResolver Sut() => new(_repo.Object);

        private void NameIsTaken(params string[] takenNames)
        {
            _repo.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync((string name) => takenNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                    ? new Organization { Name = name }
                    : null!);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task IsNameAvailable_BlankName_IsNotAvailable(string? name)
        {
            (await Sut().IsNameAvailableAsync(name)).Should().BeFalse();
        }

        [Fact]
        public async Task IsNameAvailable_FreeName_ReturnsTrue()
        {
            NameIsTaken();

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeTrue();
        }

        [Fact]
        public async Task IsNameAvailable_TakenName_ReturnsFalse()
        {
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeFalse();
        }

        [Fact]
        public async Task IsNameAvailable_TrimsBeforeChecking()
        {
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("  Acme  ")).Should().BeFalse();
        }

        [Fact]
        public async Task ResolveAvailableName_FreeBaseName_ReturnsItUnchanged()
        {
            NameIsTaken();

            (await Sut().ResolveAvailableNameAsync("Acme")).Should().Be("Acme");
        }

        [Fact]
        public async Task ResolveAvailableName_TakenBaseName_ReturnsSuffixedVariant()
        {
            NameIsTaken("Acme");

            var resolved = await Sut().ResolveAvailableNameAsync("Acme");

            resolved.Should().StartWith("Acme ").And.NotBe("Acme");
        }

        [Fact]
        public async Task ResolveAvailableName_NothingFree_ReturnsEmpty()
        {
            _repo.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Organization { Name = "taken" });

            (await Sut().ResolveAvailableNameAsync("Acme")).Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveAvailableName_BlankBaseName_ReturnsEmpty()
        {
            (await Sut().ResolveAvailableNameAsync("  ")).Should().BeEmpty();
        }

        [Fact]
        public async Task SuggestAvailableNames_ReturnsRequestedCountOfDistinctFreeNames()
        {
            NameIsTaken("Acme");

            var suggestions = await Sut().SuggestAvailableNamesAsync("Acme", 2);

            suggestions.Should().HaveCount(2);
            suggestions.Should().OnlyContain(s => s.StartsWith("Acme "));
            suggestions.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public async Task SuggestAvailableNames_NothingFree_ReturnsEmptyRatherThanLooping()
        {
            _repo.Setup(r => r.GetOrganizationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Organization { Name = "taken" });

            (await Sut().SuggestAvailableNamesAsync("Acme")).Should().BeEmpty();
        }

        [Fact]
        public async Task SuggestAvailableNames_BlankOrNonPositiveCount_ReturnsEmpty()
        {
            (await Sut().SuggestAvailableNamesAsync("  ")).Should().BeEmpty();
            (await Sut().SuggestAvailableNamesAsync("Acme", 0)).Should().BeEmpty();
        }

        private void MultiOrg(bool enabled)
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = enabled });
        }

        [Fact]
        public async Task CheckAvailability_MultiOrgDisabled_ReportsDisabledAndSkipsLookup()
        {
            MultiOrg(false);

            var result = await Sut().CheckAvailabilityAsync("Acme");

            result.MultiOrgEnabled.Should().BeFalse();
            result.IsAvailable.Should().BeFalse();
            result.Suggestions.Should().BeEmpty();
            _repo.Verify(r => r.GetOrganizationByNameAsync(It.IsAny<string>()), Times.Never,
                "a single-org tenant must not be probed for organization names");
        }

        [Fact]
        public async Task CheckAvailability_NoTenantConfiguration_ReportsDisabled()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);

            (await Sut().CheckAvailabilityAsync("Acme")).MultiOrgEnabled.Should().BeFalse();
        }

        [Fact]
        public async Task CheckAvailability_FreeName_ReportsAvailableWithNoSuggestions()
        {
            MultiOrg(true);
            NameIsTaken();

            var result = await Sut().CheckAvailabilityAsync("Acme");

            result.MultiOrgEnabled.Should().BeTrue();
            result.IsAvailable.Should().BeTrue();
            result.Suggestions.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckAvailability_TakenName_ReportsSuggestions()
        {
            MultiOrg(true);
            NameIsTaken("Acme");

            var result = await Sut().CheckAvailabilityAsync("Acme");

            result.IsAvailable.Should().BeFalse();
            result.Suggestions.Should().HaveCount(2).And.OnlyContain(s => s.StartsWith("Acme "));
        }
    }
}
