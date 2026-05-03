using Blocks.Genesis;
using Cloud.LmtService.Models.Trace;
using Cloud.LmtService.Services.Trace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{

    [ApiController]
    [Route("[controller]/[action]")]
    public class TraceController : ControllerBase
    {
        private readonly ITraceService _traceService;

        public TraceController(ITraceService traceService)
        {
            _traceService = traceService;
        }


        [HttpPost]

        public async Task<object> GetTraces([FromBody] GetTracesRequest request)
        {
            return await _traceService.GetTracesAsync(request);
        }

        [HttpGet]
    
        public async Task<object> GetTrace([FromQuery] GetTraceRequest request)
        {
            return await _traceService.GetTraceAsync(request);
        }

        [HttpPost]
 
        public async Task<object> GetOperationalAnalytics([FromBody] GetApiAnalyticsRequest request)
        {
            return await _traceService.GetOperationalAnalytics(request);
        }

        [HttpPost]
  
        public async Task<object> GetServiceAnalytics([FromBody] GetHttpStatusAnalyticsRequest request)
        {
            return await _traceService.GetServiceAnalytics(request);
        }

    }
}
