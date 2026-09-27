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
            return Failure("TenantId", "TenantId is required");
        }

        var nameClash = await _repository.FindByNameAsync(tenantId, request.Name.Trim());
        if (nameClash != null)
        {
            return Failure("Name", "A configuration with this name already exists");
        }

        var clientError = await ValidateClientAndRedirectAsync(request.ClientId, request.RedirectUri);
        if (clientError != null)
        {
            return clientError;
        }

        var roleError = await ValidateRolesAsync(request.DefaultRoles);
        if (roleError != null)
        {
            return roleError;
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
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri,
            DefaultForwardedTo = request.DefaultForwardedTo,
            CredentialMode = request.CredentialMode!.Value,
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
            return Failure("TenantId", "TenantId is required");
        }

        var entity = await _repository.GetByIdAsync(id, tenantId);
        if (entity == null)
        {
            return NotFoundMutation();
        }

        if (request.Name != null)
        {
            var trimmed = request.Name.Trim();
            var clash = await _repository.FindByNameAsync(tenantId, trimmed, excludeItemId: entity.ItemId);
            if (clash != null)
            {
                return Failure("Name", "A configuration with this name already exists");
            }

            entity.Name = trimmed;
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

        var clientId = request.ClientId ?? entity.ClientId;
        var redirectUri = request.RedirectUri ?? entity.RedirectUri;
        if (request.ClientId != null || request.RedirectUri != null)
        {
            var clientError = await ValidateClientAndRedirectAsync(clientId, redirectUri);
            if (clientError != null)
            {
                return clientError;
            }

            if (request.ClientId != null)
            {
                entity.ClientId = request.ClientId;
            }

            if (request.RedirectUri != null)
            {
                entity.RedirectUri = request.RedirectUri;
            }
        }

        if (request.DefaultForwardedTo != null)
        {
            entity.DefaultForwardedTo = request.DefaultForwardedTo;
        }

        if (request.CredentialMode.HasValue)
        {
            entity.CredentialMode = request.CredentialMode.Value;
        }

        if (request.DefaultLifetimeMinutes.HasValue)
        {
            entity.DefaultLifetimeMinutes = request.DefaultLifetimeMinutes.Value;
        }

        // Allow explicitly setting null only when the JSON property is present — for v1 the
        // nullable int? means "omit" vs "set null" cannot be distinguished; treat HasValue
        // updates only. Spec A4: accepting null on create is the default; patch may set 1.
        if (request.DefaultMaxRedemptions.HasValue)
        {
            entity.DefaultMaxRedemptions = request.DefaultMaxRedemptions;
        }

        var ctx = BlocksContext.GetContext();
        entity.LastUpdatedBy = ctx?.UserId;
        entity.LastUpdatedDate = DateTime.UtcNow;

        await _repository.ReplaceAsync(entity);
        return new BaseMutationResponse { IsSuccess = true, ItemId = entity.ItemId };
    }

    public async Task<BaseMutationResponse> ArchiveAsync(string id)
    {
        var tenantId = RequireTenantId();
        if (tenantId == null)
        {
            return Failure("TenantId", "TenantId is required");
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
            return (null, new Dictionary<string, string> { { "TenantId", "TenantId is required" } });
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
