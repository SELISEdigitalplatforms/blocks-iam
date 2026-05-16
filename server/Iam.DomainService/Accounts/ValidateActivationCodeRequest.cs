using System;
using Blocks.Genesis;

namespace Iam.DomainService.Accounts;

public class ValidateActivationCodeRequest
{
    public string ActivationCode { get; set; } = string.Empty;

}
