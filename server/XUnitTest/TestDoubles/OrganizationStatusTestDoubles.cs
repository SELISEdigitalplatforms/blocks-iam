using Iam.DomainService.Resources;
using Moq;

namespace XUnitTest.TestDoubles;

/// <summary>Organization status doubles for the token-issuance gate (spec #668).</summary>
internal static class OrganizationStatusTestDoubles
{
    /// <summary>
    /// Every looked-up organization exists and is enabled, which is what tests written before the gate assume:
    /// their expectations stay exactly as they were.
    /// </summary>
    public static Mock<IResourceRepository> WithActiveOrganizations(this Mock<IResourceRepository> repository)
    {
        repository
            .Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((IReadOnlyCollection<string> ids) => ids.Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => false, StringComparer.Ordinal));
        return repository;
    }

    /// <summary>The given organizations are disabled, the listed missing ones have no document, and any other id is active.</summary>
    public static Mock<IResourceRepository> WithOrganizationStatuses(
        this Mock<IResourceRepository> repository,
        IEnumerable<string>? disabled = null,
        IEnumerable<string>? missing = null)
    {
        var disabledSet = new HashSet<string>(disabled ?? [], StringComparer.Ordinal);
        var missingSet = new HashSet<string>(missing ?? [], StringComparer.Ordinal);
        repository
            .Setup(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((IReadOnlyCollection<string> ids) => ids
                .Distinct(StringComparer.Ordinal)
                .Where(id => !missingSet.Contains(id))
                .ToDictionary(id => id, id => disabledSet.Contains(id), StringComparer.Ordinal));
        return repository;
    }
}
