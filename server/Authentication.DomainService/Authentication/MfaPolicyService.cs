using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    public sealed class MfaPolicyService : IMfaPolicyService
    {
        private readonly IMfaConfigurationService _mfaConfigurationService;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<MfaPolicyService> _logger;

        public MfaPolicyService(
            IMfaConfigurationService mfaConfigurationService,
            IAuthenticationRepository authenticationRepository,
            ILogger<MfaPolicyService> logger)
        {
            _mfaConfigurationService = mfaConfigurationService;
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        public async Task<MfaPolicyDecision> EvaluateAsync(User user, string? clientId, CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                return new MfaPolicyDecision { Required = false, Reason = MfaPolicyReasons.NoUser };
            }

            var config = await _mfaConfigurationService.GetAsync() ?? new Configuration { UserMfaType = new List<UserMfaType>() };

            if (!config.EnableMfa)
            {
                return new MfaPolicyDecision { Required = false, Reason = MfaPolicyReasons.MfaDisabledGlobally };
            }

            var allowedMethods = config.UserMfaType ?? new List<UserMfaType>();
            var userRoles = user.Roles?.Keys?.ToList() ?? new List<string>();

            if (config.MfaExemptRoles != null
                && config.MfaExemptRoles.Count > 0
                && userRoles.Any(r => config.MfaExemptRoles.Any(er => string.Equals(er, r, StringComparison.OrdinalIgnoreCase))))
            {
                return new MfaPolicyDecision { Required = false, Reason = MfaPolicyReasons.RoleExempt };
            }

            var clientRequireMfa = false;
            List<UserMfaType>? clientAllowedMethods = null;
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
                if (client != null)
                {
                    clientRequireMfa = client.RequireMfa;
                    clientAllowedMethods = client.AllowedMfaMethods;
                }
            }

            var applicableMethods = allowedMethods
                .Where(m => clientAllowedMethods == null || clientAllowedMethods.Count == 0 || clientAllowedMethods.Contains(m))
                .ToList();

            var roleRequiresMfa = config.MfaRequiredRoles != null
                && config.MfaRequiredRoles.Count > 0
                && userRoles.Any(r => config.MfaRequiredRoles.Any(mr => string.Equals(mr, r, StringComparison.OrdinalIgnoreCase)));

            var userEnrolled = user.MfaEnabled && applicableMethods.Contains(user.UserMfaType);

            var required =
                (config.RequireMfaForAllUsers && applicableMethods.Count > 0)
                || userEnrolled
                || roleRequiresMfa
                || clientRequireMfa;

            if (!required)
            {
                return new MfaPolicyDecision
                {
                    Required = false,
                    Reason = MfaPolicyReasons.NoPolicyMatch,
                    AllowedMethods = applicableMethods,
                    PreferredMethod = user.UserMfaType
                };
            }

            var mustEnrollFirst = !userEnrolled && applicableMethods.Count > 0
                && (config.RequireMfaForAllUsers || roleRequiresMfa || clientRequireMfa);

            return new MfaPolicyDecision
            {
                Required = true,
                AllowedMethods = applicableMethods,
                PreferredMethod = user.UserMfaType != UserMfaType.None && applicableMethods.Contains(user.UserMfaType)
                    ? user.UserMfaType
                    : applicableMethods.FirstOrDefault(),
                CanUserDisable = config.AllowUserOptOut && !config.RequireMfaForAllUsers && !roleRequiresMfa,
                MustEnrollFirst = mustEnrollFirst,
                Reason = config.RequireMfaForAllUsers ? MfaPolicyReasons.GlobalPolicy
                    : roleRequiresMfa ? MfaPolicyReasons.RolePolicy
                    : clientRequireMfa ? MfaPolicyReasons.ClientPolicy
                    : MfaPolicyReasons.UserEnrolled
            };
        }
    }
}
