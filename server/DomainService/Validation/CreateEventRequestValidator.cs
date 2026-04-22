using BlocksTemplate.DomainService;
using FluentValidation;

namespace BlocksTemplate.DomainService.Validation;

public sealed class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(3, 50);

        RuleFor(x => x.Organizer)
            .NotEmpty()
            .Length(3, 50);

        RuleFor(x => x.Location)
            .NotEmpty()
            .Must(EventLocations.IsAllowed)
            .WithMessage("Location must be one of the allowed venues.");

        RuleFor(x => x.Description)
            .MaximumLength(2000);

        RuleFor(x => x)
            .Must(x => x.EndDateTime >= x.StartDateTime)
            .WithMessage("End date and time must be greater than or equal to start.");
    }
}
