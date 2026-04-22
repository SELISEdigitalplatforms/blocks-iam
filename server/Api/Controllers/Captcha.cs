using Captcha.DomainService.Captcha;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class CaptchaController : ControllerBase
    {
        private readonly ICaptchaService _captchaService;

        public CaptchaController(ICaptchaService captchaService)
        {
            _captchaService = captchaService;
        }

        [Authorize]
        [HttpPost]
        public CreateCaptchaRequestResponse Create([FromBody] CreateCaptchaRequest command)
        {
            return _captchaService.CreateCaptcha(command);
        }

        [Authorize]
        [HttpPost]
        public Task<SubmitCaptchaRequestResponse> Submit([FromBody] SubmitCaptchaRequest command)
        {
            return _captchaService.SubmitCaptchaAsync(command);
        }

        [Authorize]
        [HttpGet]
        public Task<VerifyCaptchaRequestResponse> Verify([FromQuery] VerifyCaptchaRequest query)
        {
            return _captchaService.VerifyCaptchaAsync(query);
        }
    }
}
