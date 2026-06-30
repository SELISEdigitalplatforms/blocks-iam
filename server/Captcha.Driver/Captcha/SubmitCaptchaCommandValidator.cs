using FluentValidation;

namespace Blocks.CaptchaDriver
{
    public class SubmitCaptchaCommandValidator : AbstractValidator<SubmitCaptchaRequest>,
        ISubmitCaptchaCommandValidator
    {
        public SubmitCaptchaCommandValidator()
        {
            RuleFor(c => c.Id)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty();
        }

        public virtual Task<FluentValidation.Results.ValidationResult> ValidateAsync(SubmitCaptchaRequest command)
        {
            return base.ValidateAsync(command);
        }
    }

    public interface ISubmitCaptchaCommandValidator
    {
        Task<FluentValidation.Results.ValidationResult> ValidateAsync(SubmitCaptchaRequest command);
    }
}