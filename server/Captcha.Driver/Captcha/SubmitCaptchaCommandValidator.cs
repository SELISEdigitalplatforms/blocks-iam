using FluentValidation;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Validates a <see cref="SubmitCaptchaRequest"/>.
/// </summary>
public class SubmitCaptchaCommandValidator : AbstractValidator<SubmitCaptchaRequest>
{
    /// <summary>
    /// Builds the default validation rules for <see cref="SubmitCaptchaRequest"/>.
    /// </summary>
    public SubmitCaptchaCommandValidator()
    {
        RuleFor(c => c.Id)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty();

        RuleFor(c => c.HostName)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty();
    }
}
