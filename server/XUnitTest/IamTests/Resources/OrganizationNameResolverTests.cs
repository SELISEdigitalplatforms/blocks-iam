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

        /// <summary>
        /// Every "this name is taken" answer depends on the tenant having uniqueness enabled --
        /// it is off by default, and with it off nothing is ever taken. Tests that assert a clash
        /// must therefore say so explicitly.
        /// </summary>
        private void Uniqueness(bool enforced, bool multiOrg = true)
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration
                {
                    IsMultiOrgEnabled = multiOrg,
                    IsOrgNameUniquenessEnabled = enforced
                });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task IsNameAvailable_BlankName_IsNotAvailable(string? name)
        {
            Uniqueness(true);

            (await Sut().IsNameAvailableAsync(name)).Should().BeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task IsNameAvailable_UniquenessDisabled_BlankNameIsStillNotAvailable(string? name)
        {
            Uniqueness(false);

            (await Sut().IsNameAvailableAsync(name)).Should().BeFalse(
                "a blank name is unusable for reasons that have nothing to do with uniqueness");
        }

        [Fact]
        public async Task IsNameAvailable_FreeName_ReturnsTrue()
        {
            Uniqueness(true);
            NameIsTaken();

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeTrue();
        }

        [Fact]
        public async Task IsNameAvailable_TakenName_ReturnsFalse()
        {
            Uniqueness(true);
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeFalse();
        }

        [Fact]
        public async Task IsNameAvailable_UniquenessDisabled_TakenNameIsAvailableAndNotLookedUp()
        {
            Uniqueness(false);
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeTrue();
            _repo.Verify(r => r.GetOrganizationByNameAsync(It.IsAny<string>()), Times.Never,
                "with the tenant's check off the create path accepts the name whatever holds it");
        }

        [Fact]
        public async Task IsNameAvailable_NoTenantConfiguration_TreatsUniquenessAsDisabled()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("Acme")).Should().BeTrue();
        }

        [Fact]
        public async Task IsNameAvailable_TrimsBeforeChecking()
        {
            Uniqueness(true);
            NameIsTaken("Acme");

            (await Sut().IsNameAvailableAsync("  Acme  ")).Should().BeFalse();
        }

        [Fact]
        public async Task ResolveAvailableName_FreeBaseName_ReturnsItUnchanged()
        {
            Uniqueness(true);
            NameIsTaken();

            (await Sut().ResolveAvailableNameAsync("Acme")).Should().Be("Acme");
        }

        [Fact]
        public async Task ResolveAvailableName_TakenBaseName_ReturnsSuffixedVariant()
        {
            Uniqueness(true);
            NameIsTaken("Acme");

            var resolved = await Sut().ResolveAvailableNameAsync("Acme");

            resolved.Should().StartWith("Acme ").And.NotBe("Acme");
        }

        [Fact]
        public async Task ResolveAvailableName_UniquenessDisabled_ReturnsBaseNameUnsuffixed()
        {
            Uniqueness(false);
            NameIsTaken("Acme");

            (await Sut().ResolveAvailableNameAsync("Acme")).Should().Be("Acme");
        }

        [Fact]
        public async Task ResolveAvailableName_NothingFree_ReturnsEmpty()
        {
            Uniqueness(true);
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
            Uniqueness(true);
            NameIsTaken("Acme");

            var suggestions = await Sut().SuggestAvailableNamesAsync("Acme", 2);

            suggestions.Should().HaveCount(2);
            suggestions.Should().OnlyContain(s => s.StartsWith("Acme "));
            suggestions.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public async Task SuggestAvailableNames_UniquenessDisabled_ReturnsEmpty()
        {
            Uniqueness(false);
            NameIsTaken("Acme");

            (await Sut().SuggestAvailableNamesAsync("Acme", 2)).Should().BeEmpty(
                "there is no clash to offer a way out of when the tenant does not enforce uniqueness");
        }

        [Fact]
        public async Task SuggestAvailableNames_NothingFree_ReturnsEmptyRatherThanLooping()
        {
            Uniqueness(true);
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
            Uniqueness(true, enabled);
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
            result.UniquenessEnforced.Should().BeTrue();
            result.IsAvailable.Should().BeTrue();
            result.Suggestions.Should().BeEmpty();
        }

        [Fact]
        public async Task CheckAvailability_TakenName_ReportsSuggestions()
        {
            MultiOrg(true);
            NameIsTaken("Acme");

            var result = await Sut().CheckAvailabilityAsync("Acme");

            result.UniquenessEnforced.Should().BeTrue();
            result.IsAvailable.Should().BeFalse();
            result.Suggestions.Should().HaveCount(2).And.OnlyContain(s => s.StartsWith("Acme "));
        }

        [Fact]
        public async Task CheckAvailability_UniquenessDisabled_ReportsAvailableAndUnenforced()
        {
            Uniqueness(false);
            NameIsTaken("Acme");

            var result = await Sut().CheckAvailabilityAsync("Acme");

            result.MultiOrgEnabled.Should().BeTrue();
            result.UniquenessEnforced.Should().BeFalse();
            result.IsAvailable.Should().BeTrue();
            result.Suggestions.Should().BeEmpty();
        }
    }
}
