namespace Blocks.CaptchaDriver;

/// <summary>
/// Default <see cref="ICaptchaConfigurationService"/> implementation that delegates to a repository.
/// </summary>
public sealed class CaptchaConfigurationService : ICaptchaConfigurationService
{
    private readonly ICaptchaConfigurationRepository _repository;

    public CaptchaConfigurationService(ICaptchaConfigurationRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetByNameAsync(string? configurationName)
    {
        if (configurationName is null)
        {
            throw new ArgumentNullException(nameof(configurationName));
        }
        return await _repository.GetByProviderAsync(configurationName);
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync()
    {
        return await _repository.GetCaptchaConfigurationAsync();
    }
}
