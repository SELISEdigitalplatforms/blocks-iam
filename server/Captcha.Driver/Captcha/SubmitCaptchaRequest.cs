using Blocks.Genesis;
using FluentValidation.Results;

namespace Blocks.CaptchaDriver
{
    public class SubmitCaptchaRequest
    {
        public string Id { get; set; }

        public string Value { get; set; }
    }

    public class SubmitCaptchaRequestResponse : BaseMutationResponse
    {
        public SubmitCaptchaRequestResponse(ValidationResult result) : base()
        {
            Errors = result?.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage) ?? new Dictionary<string, string>();
        }

        public string VerificationCode { get; set; }
    }
}