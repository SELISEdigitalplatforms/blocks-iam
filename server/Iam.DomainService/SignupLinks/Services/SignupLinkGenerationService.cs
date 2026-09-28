using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkGenerationService : ISignupLinkGenerationService
{
    private const string DefaultOrganizationId = "default";
    private const string TenantIdKey = "TenantId";
    private const string TenantIdRequiredMessage = "TenantId is required";

    private readonly ISignupLinkRepository _linkRepository;
    private readonly ISignupLinkConfigurationRepository _configurationRepository;
    private readonly IOidcClientRegistrationLookup _oidcLookup;
    private readonly IGrantAuthorizationService _grantAuthorization;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly IValidator<GenerateSignupLinkRequest> _generateValidator;
    private readonly IValidator<QuerySignupLinksRequest> _queryValidator;
    private readonly IValidator<RevokeSignupLinksByConfigurationRequest> _revokeByConfigValidator;
    private readonly ILogger<SignupLinkGenerationService> _logger;

    public SignupLinkGenerationService(
        SignupLinkGenerationRepositories repositories,
        SignupLinkGenerationValidators validators,
        ILogger<SignupLinkGenerationService> logger)
    {
        _linkRepository = repositories.Links;
        _configurationRepository = repositories.Configurations;
        _oidcLookup = repositories.Oidc;
        _grantAuthorization = repositories.GrantAuthorization;
        _userRepository = repositories.Users;
        _configuration = validators.Configuration;
        _generateValidator = validators.GenerateValidator;
        _queryValidator = validators.QueryValidator;
        _revokeByConfigValidator = validators.RevokeByConfigValidator;
        _logger = logger;
    }

    public async Task<GenerateSignupLinkResult> GenerateAsync(GenerateSignupLinkRequest request)
    {
        var validation = await _generateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return GenerateSignupLinkResult.Fail(
                400,
                validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
        }

        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return GenerateSignupLinkResult.Fail(400, TenantIdKey, TenantIdRequiredMessage);
        }

        var ctx = BlocksContext.GetContext();
        var orgResolution = ResolveOrganization(ctx?.OrganizationId, request.OrganizationId);
        if (orgResolution.Error != null)
        {
            return GenerateSignupLinkResult.Fail(400, "OrganizationId", orgResolution.Error);
        }

        var config = await _configurationRepository.GetByIdAsync(request.ConfigurationId, tenantId);
        if (config == null || !config.IsActive)
        {
            return GenerateSignupLinkResult.Fail(400, "ConfigurationId", "Not found or inactive");
        }

        var clientError = await ValidateClientAndRedirectAsync(config.ClientId, config.RedirectUri);
        if (clientError != null)
        {
            return clientError;
        }

        var roles = UsePayloadOrDefault(request.Roles, config.DefaultRoles);
        var permissions = UsePayloadOrDefault(request.Permissions, config.DefaultPermissions);

        var deniedRole = await _grantAuthorization.FindUngrantableRoleAsync(roles);
        if (deniedRole != null)
        {
            return GenerateSignupLinkResult.Fail(403, "Roles", $"You cannot grant the role: {deniedRole}");
        }

        var deniedPermission = _grantAuthorization.FindUngrantablePermission(permissions);
        if (deniedPermission != null)
        {
            return GenerateSignupLinkResult.Fail(403, "Permissions", $"You cannot grant the permission: {deniedPermission}");
        }

        var forwardedTo = !string.IsNullOrWhiteSpace(request.ForwardedTo)
            ? request.ForwardedTo
            : config.DefaultForwardedTo;

        var lifetimeMinutes = request.ExpiresInMinutes
            ?? config.DefaultLifetimeMinutes;
        if (lifetimeMinutes < SignupLinkConfigurationValidation.MinLifetimeMinutes
            || lifetimeMinutes > SignupLinkConfigurationValidation.MaxLifetimeMinutes)
        {
            return GenerateSignupLinkResult.Fail(400, "ExpiresInMinutes", "Must be between 5 and 10080");
        }

        var email = NormalizeEmail(request.Email);
        var existingUser = await _userRepository.GetUserByEmailAsync(email);
        var emailAlreadyExists = existingUser != null;

        var (code, codeHash) = MintCode();
        var now = DateTime.UtcNow;
        var linkId = Guid.NewGuid().ToString();
        var expiresAt = now.AddMinutes(lifetimeMinutes);

        var entity = new SignupLink
        {
            ItemId = linkId,
            CodeHash = codeHash,
            TenantId = tenantId,
            ConfigurationId = config.ItemId,
            OrganizationId = orgResolution.OrganizationId!,
            Roles = roles.ToList(),
            Permissions = permissions.ToList(),
            ClientId = config.ClientId,
            RedirectUri = config.RedirectUri,
            ForwardedTo = forwardedTo,
            CredentialMode = config.CredentialMode,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en-US" : request.Language!,
            ExpiresAtUtc = expiresAt,
            MaxRedemptions = 1,
            RedemptionCount = 0,
            Status = SignupLinkStatus.Active,
            CreatedUserId = null,
            CreatedBy = ctx?.UserId,
            CreatedByClientId = ctx?.ClientId,
            CreatedByOrganizationId = ctx?.OrganizationId,
            CreatedDate = now,
            LastUpdatedDate = now,
            LastUpdatedBy = ctx?.UserId
        };

        await _linkRepository.InsertAsync(entity);

        // Never log the code, URL, or email (C5 / CodeQL exposure + log-forging).
        _logger.LogInformation("Signup link generated");

        var baseUrl = IamHelper.GetConfiguredIamBaseUrl(_configuration).TrimEnd('/');
        var url = $"{baseUrl}/oidc/join/{tenantId}#link={code}";

        return GenerateSignupLinkResult.Ok(new GenerateSignupLinkResponse
        {
            LinkId = linkId,
            Url = url,
            ExpiresAtUtc = expiresAt,
            EmailAlreadyExists = emailAlreadyExists
        });
    }

    public async Task<(SignupLinkListResponse? Response, Dictionary<string, string>? Errors)> QueryAsync(
        QuerySignupLinksRequest request)
    {
        var validation = await _queryValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return (null, validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
        }

        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return (null, new Dictionary<string, string> { { TenantIdKey, TenantIdRequiredMessage } });
        }

        var (items, total) = await _linkRepository.QueryAsync(tenantId, request);
        return (new SignupLinkListResponse
        {
            Items = items.Select(MapListItem).ToList(),
            TotalCount = total
        }, null);
    }

    public async Task<RevokeSignupLinkResponse> RevokeAsync(string linkId)
    {
        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return new RevokeSignupLinkResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { TenantIdKey, TenantIdRequiredMessage } }
            };
        }

        var entity = await _linkRepository.GetByIdAsync(linkId, tenantId);
        if (entity == null)
        {
            return new RevokeSignupLinkResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
            };
        }

        if (entity.Status != SignupLinkStatus.Active)
        {
            return new RevokeSignupLinkResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "Status", "Only Active links can be revoked" } }
            };
        }

        var ctx = BlocksContext.GetContext();
        var now = DateTime.UtcNow;
        entity.Status = SignupLinkStatus.Revoked;
        entity.RevokedAtUtc = now;
        entity.RevokedBy = ctx?.UserId;
        entity.LastUpdatedDate = now;
        entity.LastUpdatedBy = ctx?.UserId;
        await _linkRepository.ReplaceAsync(entity);

        _logger.LogInformation("Signup link revoked");

        return new RevokeSignupLinkResponse { IsSuccess = true, ItemId = entity.ItemId };
    }

    public async Task<RevokeSignupLinksByConfigurationResponse> RevokeByConfigurationAsync(
        RevokeSignupLinksByConfigurationRequest request)
    {
        var validation = await _revokeByConfigValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return new RevokeSignupLinksByConfigurationResponse
            {
                IsSuccess = false,
                Errors = validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
            };
        }

        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return new RevokeSignupLinksByConfigurationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { TenantIdKey, TenantIdRequiredMessage } }
            };
        }

        var ctx = BlocksContext.GetContext();
        var now = DateTime.UtcNow;
        var count = await _linkRepository.RevokeActiveByConfigurationAsync(
            tenantId,
            request.ConfigurationId,
            ctx?.UserId ?? string.Empty,
            now);

        _logger.LogInformation("Signup links revoked by configuration. RevokedCount={RevokedCount}", count);

        return new RevokeSignupLinksByConfigurationResponse
        {
            IsSuccess = true,
            RevokedCount = count
        };
    }

    private async Task<GenerateSignupLinkResult?> ValidateClientAndRedirectAsync(string clientId, string redirectUri)
    {
        var client = await _oidcLookup.GetByClientIdAsync(clientId);
        if (client == null || !client.IsActive)
        {
            return GenerateSignupLinkResult.Fail(400, "ClientId", "Client not found or inactive");
        }

        var registered = client.RedirectUris.Any(u =>
            string.Equals(u, redirectUri, StringComparison.Ordinal));
        if (!registered)
        {
            return GenerateSignupLinkResult.Fail(400, "RedirectUri", "RedirectUri is not registered for this client");
        }

        return null;
    }

    public static (string? OrganizationId, string? Error) ResolveOrganization(
        string? tokenOrganizationId,
        string? payloadOrganizationId)
    {
        var tokenOrg = string.IsNullOrWhiteSpace(tokenOrganizationId)
            ? DefaultOrganizationId
            : tokenOrganizationId;

        var payloadPresent = !string.IsNullOrWhiteSpace(payloadOrganizationId);

        if (!string.Equals(tokenOrg, DefaultOrganizationId, StringComparison.OrdinalIgnoreCase))
        {
            if (payloadPresent
                && !string.Equals(tokenOrg, payloadOrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "Organization must match the caller's organization");
            }

            return (tokenOrg, null);
        }

        if (!payloadPresent)
        {
            return (null, "OrganizationId is required");
        }

        return (payloadOrganizationId!.Trim(), null);
    }

    private static List<string> UsePayloadOrDefault(List<string>? payload, List<string>? defaults)
    {
        if (payload == null || payload.Count == 0)
        {
            return defaults?.ToList() ?? [];
        }

        return payload.ToList();
    }

    private static (string Code, string CodeHash) MintCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var code = Base64UrlEncode(bytes);
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        return (code, hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? RequireTenantId()
    {
        var tenantId = BlocksContext.GetContext()?.TenantId;
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }

    private static SignupLinkListItemResponse MapListItem(SignupLink entity) => new()
    {
        LinkId = entity.ItemId,
        Email = entity.Email,
        FirstName = entity.FirstName,
        LastName = entity.LastName,
        OrganizationId = entity.OrganizationId ?? string.Empty,
        Roles = entity.Roles?.ToList() ?? [],
        ConfigurationId = entity.ConfigurationId,
        Status = entity.Status,
        ExpiresAtUtc = entity.ExpiresAtUtc,
        CreatedDate = entity.CreatedDate,
        CreatedBy = entity.CreatedBy
    };
}
