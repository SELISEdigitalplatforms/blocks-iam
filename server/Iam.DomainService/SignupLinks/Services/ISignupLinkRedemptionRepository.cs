namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkRedemptionRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLinkRedemption entity);
}
