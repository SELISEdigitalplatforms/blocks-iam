using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class QuerySignupLinksValidator : AbstractValidator<QuerySignupLinksRequest>
{
    public QuerySignupLinksValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
