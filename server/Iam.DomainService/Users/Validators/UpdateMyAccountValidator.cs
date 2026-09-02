using FluentValidation;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// Mirrors the length rules of <see cref="UpdateUserValidator"/> for the self-service surface.
    /// There is no <c>ItemId</c> rule: the subject comes from the authenticated context, so there
    /// is no client-supplied identifier to validate.
    /// <para>
    /// Every rule is conditioned on the field being present. Under the sparse contract an absent
    /// field is not an empty one, so an omitted name must not be measured against a length limit.
    /// </para>
    /// </summary>
    public class UpdateMyAccountValidator : AbstractValidator<UpdateMyAccountRequest>
    {
        public UpdateMyAccountValidator()
        {
            RuleFor(u => u.FirstName)
                .MaximumLength(150).WithMessage("Maximum character limit 150 exceeded")
                .When(u => !string.IsNullOrWhiteSpace(u.FirstName));

            RuleFor(u => u.LastName)
                .MaximumLength(150).WithMessage("Maximum character limit 150 exceeded")
                .When(u => !string.IsNullOrWhiteSpace(u.LastName));
        }
    }
}
