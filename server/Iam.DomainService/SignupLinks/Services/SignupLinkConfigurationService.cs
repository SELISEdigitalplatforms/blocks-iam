using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkConfigurationService : ISignupLinkConfigurationService
{
    private readonly ISignupLinkConfigurationRepository _repository;
    private readonly IOidcClientRegistrationLookup _oidcLookup;
    private readonly IResourceRepository _resourceRepository;
    private readonly IValidator<CreateSignupLinkConfigurationRequest> _createValidator;
    private readonly IValidator<UpdateSignupLinkConfigurationRequest> _updateValidator;
    private readonly IValidator<QuerySignupLinkConfigurationsRequest> _queryValidator;

    private const string TenantIdKey = "TenantId";
    private const string TenantIdRequiredMessage = "TenantId is required";

    public SignupLinkConfigurationService(
        ISignupLinkConfigurationRepository repository,
        IOidcClientRegistrationLookup oidcLookup,
        IResourceRepository resourceRepository,
        IValidator<CreateSignupLinkConfigurationRequest> createValidator,
        IValidator<UpdateSignupLinkConfigurationRequest> updateValidator,
        IValidator<QuerySignupLinkConfigurationsRequest> queryValidator)
    {
        _repository = repository;
        _oidcLookup = oidcLookup;
        _resourceRepository = resourceRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _queryValidator = queryValidator;
    }

    public async Task<BaseMutationResponse> CreateAsync(CreateSignupLinkConfigurationRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Failure(validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
        }

        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return Failure(TenantIdKey, TenantIdRequiredMessage);
        }

        var nameClash = await _repository.FindByNameAsync(tenantId, request.Name.Trim());
        if (nameClash != null)
        {
            return Failure("Name", "A configuration with this name already exists");
        }

        var mode = SignupLinkConfigurationValidation.Resolve(request.Mode);
        if (mode == SignupLinkMode.Oidc)
        {
            var clientError = await ValidateClientAndRedirectAsync(request.ClientId, request.RedirectUri);
            if (clientError != null)
            {
                return clientError;
            }
        }

        var roleError = await ValidateRolesAsync(request.DefaultRoles);
        if (roleError != null)
        {
            return roleError;
        }

        var signInError = ValidateSignInAfterActivation(
            request.SignInAfterActivation, request.CredentialMode!.Value);
        if (signInError != null)
        {
            return signInError;
        }

        var ctx = BlocksContext.GetContext();
        var now = DateTime.UtcNow;
        var entity = new SignupLinkConfiguration
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description,
            DefaultRoles = request.DefaultRoles?.ToList() ?? [],
            DefaultPermissions = request.DefaultPermissions?.ToList() ?? [],
            ClientId = mode == SignupLinkMode.Oidc ? request.ClientId : string.Empty,
            RedirectUri = mode == SignupLinkMode.Oidc ? request.RedirectUri : string.Empty,
            DefaultForwardedTo = request.DefaultForwardedTo,
            CredentialMode = request.CredentialMode!.Value,
            Mode = mode,
            JoinUrl = mode == SignupLinkMode.Embedded ? request.JoinUrl : null,
            SignInAfterActivation = request.SignInAfterActivation ?? false,
            DefaultLifetimeMinutes = request.DefaultLifetimeMinutes
                ?? SignupLinkConfigurationValidation.DefaultLifetimeMinutes,
            DefaultMaxRedemptions = request.DefaultMaxRedemptions,
            IsActive = true,
            CreatedBy = ctx?.UserId,
            CreatedDate = now,
            LastUpdatedBy = ctx?.UserId,
            LastUpdatedDate = now
        };

        await _repository.InsertAsync(entity);
        return new BaseMutationResponse { IsSuccess = true, ItemId = entity.ItemId };
    }

    public async Task<BaseMutationResponse> UpdateAsync(string id, UpdateSignupLinkConfigurationRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Failure(validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
        }

        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return Failure(TenantIdKey, TenantIdRequiredMessage);
        }

        var entity = await _repository.GetByIdAsync(id, tenantId);
        if (entity == null)
        {
            return NotFoundMutation();
        }

        var applyError = await ApplyUpdateFieldsAsync(entity, tenantId, request);
        if (applyError != null)
        {
            return applyError;
        }

        var ctx = BlocksContext.GetContext();
        entity.LastUpdatedBy = ctx?.UserId;
        entity.LastUpdatedDate = DateTime.UtcNow;

        await _repository.ReplaceAsync(entity);
        return new BaseMutationResponse { IsSuccess = true, ItemId = entity.ItemId };
    }

    private async Task<BaseMutationResponse?> ApplyUpdateFieldsAsync(
        SignupLinkConfiguration entity,
        string tenantId,
        UpdateSignupLinkConfigurationRequest request)
    {
        var nameError = await ApplyNameAsync(entity, tenantId, request.Name);
        if (nameError != null)
        {
            return nameError;
        }

        if (request.Description != null)
        {
            entity.Description = request.Description;
        }

        if (request.DefaultRoles != null)
        {
            var roleError = await ValidateRolesAsync(request.DefaultRoles);
            if (roleError != null)
            {
                return roleError;
            }

            entity.DefaultRoles = request.DefaultRoles.ToList();
        }

        if (request.DefaultPermissions != null)
        {
            entity.DefaultPermissions = request.DefaultPermissions.ToList();
        }

        var clientError = await ApplyClientRedirectAsync(entity, request);
        if (clientError != null)
        {
            return clientError;
        }

        if (request.DefaultForwardedTo != null)
        {
            entity.DefaultForwardedTo = request.DefaultForwardedTo;
        }

        if (request.CredentialMode.HasValue)
        {
            entity.CredentialMode = request.CredentialMode.Value;
        }

        // Checked against the credential mode the patch leaves behind, not the one it arrived
        // with: switching to Passwordless and leaving the flag set would otherwise persist a
        // combination that create refuses.
        var signInError = ValidateSignInAfterActivation(
            request.SignInAfterActivation ?? entity.SignInAfterActivation, entity.CredentialMode);
        if (signInError != null)
        {
            return signInError;
        }

        if (request.SignInAfterActivation.HasValue)
        {
            entity.SignInAfterActivation = request.SignInAfterActivation.Value;
        }

        if (request.DefaultLifetimeMinutes.HasValue)
        {
            entity.DefaultLifetimeMinutes = request.DefaultLifetimeMinutes.Value;
        }

        // Spec A4: patch may set max redemptions to 1; omit leaves unchanged.
        if (request.DefaultMaxRedemptions.HasValue)
        {
            entity.DefaultMaxRedemptions = request.DefaultMaxRedemptions;
        }

        return null;
    }

    private async Task<BaseMutationResponse?> ApplyNameAsync(
        SignupLinkConfiguration entity,
        string tenantId,
        string? name)
    {
        if (name == null)
        {
            return null;
        }

        var trimmed = name.Trim();
        var clash = await _repository.FindByNameAsync(tenantId, trimmed, excludeItemId: entity.ItemId);
        if (clash != null)
        {
            return Failure("Name", "A configuration with this name already exists");
        }

        entity.Name = trimmed;
        return null;
    }

    /// <summary>
    /// Mode, client, redirect and join URL are one consistent set, and which of them are legal
    /// depends on the mode the document ends up in -- known only after merging the request
    /// with the stored entity, which is why this rule is here and not in the validator.
    /// </summary>
    private async Task<BaseMutationResponse?> ApplyClientRedirectAsync(
        SignupLinkConfiguration entity,
        UpdateSignupLinkConfigurationRequest request)
    {
        var mode = request.Mode ?? entity.Mode;

        if (mode == SignupLinkMode.Embedded)
        {
            if (!string.IsNullOrWhiteSpace(request.ClientId))
            {
                return Failure("ClientId", "ClientId must be empty in embedded mode");
            }

            if (!string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                return Failure("RedirectUri", "RedirectUri must be empty in embedded mode");
            }

            entity.Mode = mode;
            entity.ClientId = string.Empty;
            entity.RedirectUri = string.Empty;
            if (request.JoinUrl != null)
            {
                entity.JoinUrl = request.JoinUrl;
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.JoinUrl))
        {
            return Failure("JoinUrl", "JoinUrl applies only to embedded configurations");
        }

        var clientId = request.ClientId ?? entity.ClientId;
        var redirectUri = request.RedirectUri ?? entity.RedirectUri;

        // Switching an embedded configuration to OIDC needs a client and a redirect, which
        // it has never carried, so they must arrive in the same request.
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Failure("ClientId", "ClientId is required in OIDC mode");
        }

        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Failure("RedirectUri", "RedirectUri is required in OIDC mode");
        }

        if (request.ClientId == null && request.RedirectUri == null && request.Mode == null)
        {
            return null;
        }

        var clientError = await ValidateClientAndRedirectAsync(clientId, redirectUri);
        if (clientError != null)
        {
            return clientError;
        }

        entity.Mode = mode;
        entity.ClientId = clientId;
        entity.RedirectUri = redirectUri;
        entity.JoinUrl = null;

        return null;
    }

    public async Task<BaseMutationResponse> ArchiveAsync(string id)
    {
        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return Failure(TenantIdKey, TenantIdRequiredMessage);
        }

        var entity = await _repository.GetByIdAsync(id, tenantId);
        if (entity == null)
        {
            return NotFoundMutation();
        }

        entity.IsActive = false;
        var ctx = BlocksContext.GetContext();
        entity.LastUpdatedBy = ctx?.UserId;
        entity.LastUpdatedDate = DateTime.UtcNow;
        await _repository.ReplaceAsync(entity);
        return new BaseMutationResponse { IsSuccess = true, ItemId = entity.ItemId };
    }

    public async Task<(SignupLinkConfigurationResponse? Response, bool NotFound)> GetByIdAsync(string id)
    {
        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return (null, true);
        }

        var entity = await _repository.GetByIdAsync(id, tenantId);
        if (entity == null)
        {
            return (null, true);
        }

        return (Map(entity), false);
    }

    public async Task<(SignupLinkConfigurationListResponse? Response, Dictionary<string, string>? Errors)> QueryAsync(
        QuerySignupLinkConfigurationsRequest request)
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

        var (items, total) = await _repository.QueryAsync(tenantId, request);
        return (new SignupLinkConfigurationListResponse
        {
            Items = items.Select(Map).ToList(),
            TotalCount = total
        }, null);
    }

    private async Task<BaseMutationResponse?> ValidateClientAndRedirectAsync(string clientId, string redirectUri)
    {
        var client = await _oidcLookup.GetByClientIdAsync(clientId);
        if (client == null || !client.IsActive)
        {
            return Failure("ClientId", "Client not found or inactive");
        }

        var registered = client.RedirectUris.Any(u =>
            string.Equals(u, redirectUri, StringComparison.Ordinal));
        if (!registered)
        {
            return Failure("RedirectUri", "RedirectUri is not registered for this client");
        }

        return null;
    }

    private async Task<BaseMutationResponse?> ValidateRolesAsync(IEnumerable<string>? roles)
    {
        if (roles == null)
        {
            return null;
        }

        foreach (var slug in roles)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return Failure("DefaultRoles", "Unknown role: ");
            }

            var matches = await _resourceRepository.GetNonArchivedRolesBySlugAsync(slug);
            if (matches == null || matches.Count == 0)
            {
                return Failure("DefaultRoles", $"Unknown role: {slug}");
            }
        }

        return null;
    }

    private static string? RequireTenantId()
    {
        var tenantId = BlocksContext.GetContext()?.TenantId;
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }

    /// <summary>
    /// SignInAfterActivation only means anything for PasswordRequired: Passwordless never
    /// mints an activation key, so there is no activation for it to act on. Refused rather
    /// than stored-and-ignored -- an inert flag in the portal is a promise the product does
    /// not keep.
    /// </summary>
    private static BaseMutationResponse? ValidateSignInAfterActivation(
        bool? signInAfterActivation,
        SignupLinkCredentialMode credentialMode)
    {
        if (signInAfterActivation == true && credentialMode != SignupLinkCredentialMode.PasswordRequired)
        {
            return Failure(
                "SignInAfterActivation",
                "SignInAfterActivation applies only to PasswordRequired configurations");
        }

        return null;
    }

    private static SignupLinkConfigurationResponse Map(SignupLinkConfiguration entity) => new()
    {
        ItemId = entity.ItemId,
        Name = entity.Name,
        Description = entity.Description,
        DefaultRoles = entity.DefaultRoles?.ToList() ?? [],
        DefaultPermissions = entity.DefaultPermissions?.ToList() ?? [],
        ClientId = entity.ClientId,
        RedirectUri = entity.RedirectUri,
        DefaultForwardedTo = entity.DefaultForwardedTo,
        CredentialMode = entity.CredentialMode,
        Mode = entity.Mode,
        JoinUrl = entity.JoinUrl,
        SignInAfterActivation = entity.SignInAfterActivation,
        DefaultLifetimeMinutes = entity.DefaultLifetimeMinutes,
        DefaultMaxRedemptions = entity.DefaultMaxRedemptions,
        IsActive = entity.IsActive,
        CreatedDate = entity.CreatedDate,
        LastUpdatedDate = entity.LastUpdatedDate
    };

    private static BaseMutationResponse Failure(string key, string message) => new()
    {
        IsSuccess = false,
        Errors = new Dictionary<string, string> { { key, message } }
    };

    private static BaseMutationResponse Failure(Dictionary<string, string> errors) => new()
    {
        IsSuccess = false,
        Errors = errors
    };

    private static BaseMutationResponse NotFoundMutation() => new()
    {
        IsSuccess = false,
        Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
    };
}
