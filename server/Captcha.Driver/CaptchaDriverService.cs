namespace Blocks.CaptchaDriver;

/// <summary>
/// Default <see cref="ICaptchaDriverService"/> implementation that delegates to <see cref="ICaptchaService"/>.
/// </summary>
public sealed class CaptchaDriverService : ICaptchaDriverService
{
    private readonly ICaptchaService _captchaService;

    public CaptchaDriverService(ICaptchaService captchaService)
    {
        _captchaService = captchaService;
    }

    /// <inheritdoc />
    public Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _captchaService.SubmitCaptchaAsync(command);
    }

    /// <inheritdoc />
    public Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _captchaService.VerifyCaptchaAsync(query);
    }
}
