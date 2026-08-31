using System.Security.Cryptography;
using Authentication.DomainService.OAuth;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// Why the provisioning attempt ended the way it did. The caller needs to tell a refusal
    /// apart from a failure: a refusal is the tenant's policy answering, a failure is ours.
    /// </summary>
    public enum SsoProvisioningOutcome
    {
        /// <summary>The email already had an account. This was a login, not a signup.</summary>
        ExistingUser = 0,

        /// <summary>A new account was created for the email.</summary>
        Provisioned = 1,

        /// <summary>No account existed and the tenant has SSO signup switched off.</summary>
        SignupDisabled = 2,

        /// <summary>The provider gave us nothing usable, or the write failed.</summary>
        Failed = 3
    }

    public sealed class SsoProvisioningResult
    {
        public required SsoProvisioningOutcome Outcome { get; init; }
        public User? User { get; init; }

        public static SsoProvisioningResult From(SsoProvisioningOutcome outcome, User? user = null)
            => new() { Outcome = outcome, User = user };
    }

    public interface ISsoUserProvisioningService
    {
        /// <summary>
        /// Resolves the Blocks user behind an external identity, creating one when the tenant
        /// allows SSO signup. Shared by both social callbacks so the two cannot drift apart.
        /// </summary>
        Task<SsoProvisioningResult> ResolveOrProvisionAsync(IExternalUserData externalUser, string provider);
    }

    public sealed class SsoUserProvisioningService : ISsoUserProvisioningService
    {
        private const string DefaultOrganizationId = "default";

        /// <summary>
        /// Appended to a derived organization name when the name is already taken.
        /// Ambiguous characters (0/O, 1/I) are excluded so the suffix stays readable.
        /// </summary>
        private const string OrgSuffixAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int OrgSuffixLength = 5;
        private const int OrgNameMaxAttempts = 5;
        private const int MaxPersonNameLength = 60;

        private readonly ILogger<SsoUserProvisioningService> _logger;
        private readonly IUserRepository _userRepository;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly IResourceRepository _resourceRepository;

        public SsoUserProvisioningService(
            ILogger<SsoUserProvisioningService> logger,
            IUserRepository userRepository,
            IResourceMutationService resourceMutationService,
            IResourceRepository resourceRepository)
        {
            _logger = logger;
            _userRepository = userRepository;
            _resourceMutationService = resourceMutationService;
            _resourceRepository = resourceRepository;
        }

        public async Task<SsoProvisioningResult> ResolveOrProvisionAsync(IExternalUserData externalUser, string provider)
        {
            try
            {
                var normalizedEmail = NormalizeEmail(externalUser.Email);
                if (string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    _logger.LogError("Provider {Provider} returned no email; cannot resolve a user", provider);
                    return SsoProvisioningResult.From(SsoProvisioningOutcome.Failed);
                }

                var existingUser = await _userRepository.GetUserByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    // Existing members keep the organizations they already belong to --
                    // this is a login, not a signup.
                    return SsoProvisioningResult.From(SsoProvisioningOutcome.ExistingUser, existingUser);
                }

                var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

                // The tenant's switch, not ours. Off means an unknown email is turned away exactly
                // as it was before auto-provisioning existed.
                if (!(tenantConfig?.IsSSoSignUpEnabled ?? false))
                {
                    _logger.LogInformation(
                        "SSO signup is disabled for this tenant; refusing to provision {Email} from {Provider}",
                        normalizedEmail,
                        provider);
                    return SsoProvisioningResult.From(SsoProvisioningOutcome.SignupDisabled);
                }

                // Id is needed up front so the organization records this user as creator.
                var newUserId = Guid.NewGuid().ToString();
                var organizationId = await ResolveSignupOrganizationAsync(externalUser, tenantConfig, newUserId);

                var createdUser = await CreateUserAsync(
                    newUserId,
                    normalizedEmail,
                    externalUser,
                    provider,
                    organizationId,
                    tenantConfig);

                if (createdUser.user == null)
                {
                    return SsoProvisioningResult.From(SsoProvisioningOutcome.Failed);
                }

                return SsoProvisioningResult.From(
                    createdUser.wasCreated ? SsoProvisioningOutcome.Provisioned : SsoProvisioningOutcome.ExistingUser,
                    createdUser.user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving or provisioning SSO user for provider {Provider}", provider);
                return SsoProvisioningResult.From(SsoProvisioningOutcome.Failed);
            }
        }

        /// <summary>
        /// Where a brand-new SSO user is placed.
        /// <list type="bullet">
        /// <item>Multi-org off: the default organization -- a single-org tenant has only one.</item>
        /// <item>Multi-org on and creation allowed from signup: a new organization named after them.</item>
        /// <item>Anything else, creation failure included: <c>null</c>, meaning no organization at all.
        /// Falling back to the default organization here would hand the user a membership the tenant
        /// deliberately did not grant.</item>
        /// </list>
        /// </summary>
        private async Task<string?> ResolveSignupOrganizationAsync(
            IExternalUserData externalUser,
            TenantConfiguration? tenantConfig,
            string creatorUserId)
        {
            if (!(tenantConfig?.IsMultiOrgEnabled ?? false))
            {
                return DefaultOrganizationId;
            }

            if (!tenantConfig!.AllowOrgCreationFromSignup)
            {
                _logger.LogInformation(
                    "Organization creation from signup is disabled; provisioning {Email} with no organization",
                    NormalizeEmail(externalUser.Email));
                return null;
            }

            try
            {
                var organizationName = await ResolveAvailableOrganizationNameAsync(externalUser);
                if (string.IsNullOrWhiteSpace(organizationName))
                {
                    _logger.LogWarning("Could not find an available organization name; provisioning with no organization");
                    return null;
                }

                var result = await _resourceMutationService.CreateOrganizationAsync(
                    new CreateOrganizationRequest
                    {
                        Name = organizationName,
                        CreatedFrom = CreatedFrom.ConstructSignup
                    },
                    creatorUserId);

                if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ItemId))
                {
                    _logger.LogWarning(
                        "Organization creation failed for SSO signup {Email}; provisioning with no organization",
                        NormalizeEmail(externalUser.Email));
                    return null;
                }

                return result.ItemId;
            }
            catch (Exception ex)
            {
                // A login must not break because an organization could not be made.
                _logger.LogError(ex, "Error creating organization during SSO signup; provisioning with no organization");
                return null;
            }
        }

        /// <summary>
        /// Writes the new user, re-checking the email immediately beforehand. The lookup that
        /// brought us here happened before the organization was created, so it is stale by the
        /// time we insert; without this a second callback in that window would create a duplicate
        /// account for one email. There is no unique index behind this -- the check is the guard.
        /// </summary>
        private async Task<(User? user, bool wasCreated)> CreateUserAsync(
            string userId,
            string normalizedEmail,
            IExternalUserData externalUser,
            string provider,
            string? organizationId,
            TenantConfiguration? tenantConfig)
        {
            var raced = await _userRepository.GetUserByEmailAsync(normalizedEmail);
            if (raced != null)
            {
                _logger.LogInformation(
                    "User {Email} appeared while provisioning; reusing it instead of creating a duplicate",
                    normalizedEmail);
                return (raced, false);
            }

            var roles = tenantConfig?.DefaultRolesForNewUserOnSignUp ?? new List<string>();
            var permissions = tenantConfig?.DefaultPermissionsForNewUserOnSignUp ?? new List<string>();

            var user = new User
            {
                ItemId = userId,
                Email = normalizedEmail,
                UserName = normalizedEmail,
                FirstName = externalUser.FirstName ?? externalUser.DisplayName,
                LastName = externalUser.LastName,
                ProfileImageUrl = externalUser.ProfileImageUrl,
                PhoneNumber = externalUser.PhoneNumber,
                Platform = provider,
                Active = true,
                IsVerified = true,
                Status = UserLifecycleStatus.Active,
                StatusReason = "social_signup",
                ProvisioningSource = UserProvisioningSource.Social,

                // No organization means no role or permission scope to hang entries on, so all
                // three stay empty together rather than growing a "" key.
                Roles = organizationId == null
                    ? new Dictionary<string, List<string>>()
                    : new Dictionary<string, List<string>> { { organizationId, roles } },
                Permissions = organizationId == null
                    ? new Dictionary<string, List<string>>()
                    : new Dictionary<string, List<string>> { { organizationId, permissions } },
                OrganizationIds = organizationId == null
                    ? new List<string>()
                    : new List<string> { organizationId },

                Attributes = provider == "microsoft"
                    ? new Dictionary<string, object>
                    {
                        { "Department", externalUser.Department },
                        { "EmployeeId", externalUser.EmployeeId },
                        { "ExternalProviderUserId", externalUser.ExternalProviderUserId }
                    }
                    : new Dictionary<string, object>(),

                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow
            };

            if (!await _userRepository.CreateUserAsync(user))
            {
                _logger.LogError("Failed to write provisioned SSO user {Email}", normalizedEmail);
                return (null, false);
            }

            _logger.LogInformation(
                "Provisioned SSO user {Email} from {Provider} into organization {OrganizationId}",
                normalizedEmail,
                provider,
                organizationId ?? "(none)");

            return (user, true);
        }

        /// <summary>
        /// "{FirstName} {LastName} Organization", with a random suffix appended when that
        /// name is taken. Organization names are unique case-insensitively, so a plain
        /// duplicate would otherwise fail the signup outright.
        /// </summary>
        private async Task<string> ResolveAvailableOrganizationNameAsync(IExternalUserData externalUser)
        {
            var baseName = BuildOrganizationBaseName(externalUser);

            if (await _resourceRepository.GetOrganizationByNameAsync(baseName) == null)
            {
                return baseName;
            }

            for (var attempt = 0; attempt < OrgNameMaxAttempts; attempt++)
            {
                var candidate = $"{baseName} {GenerateOrgSuffix()}";
                if (await _resourceRepository.GetOrganizationByNameAsync(candidate) == null)
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string BuildOrganizationBaseName(IExternalUserData externalUser)
        {
            // FirstName falls back to DisplayName at the call site; when the provider sends
            // neither, a random token stands in so the name is never the bare " Organization".
            var parts = new[] { externalUser.FirstName, externalUser.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim());

            var personName = string.Join(" ", parts);

            if (string.IsNullOrWhiteSpace(personName))
            {
                personName = GenerateOrgSuffix();
            }

            if (personName.Length > MaxPersonNameLength)
            {
                personName = personName[..MaxPersonNameLength].TrimEnd();
            }

            return $"{personName} Organization";
        }

        private static string GenerateOrgSuffix()
        {
            return RandomNumberGenerator.GetString(OrgSuffixAlphabet, OrgSuffixLength);
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }
    }
}
