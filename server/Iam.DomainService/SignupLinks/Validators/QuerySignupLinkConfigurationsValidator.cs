using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class QuerySignupLinkConfigurationsValidator : AbstractValidator<QuerySignupLinkConfigurationsRequest>
{
    public QuerySignupLinkConfigurationsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
