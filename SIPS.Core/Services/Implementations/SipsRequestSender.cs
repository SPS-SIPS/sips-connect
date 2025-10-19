using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Abstractions;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;

namespace SIPS.Core.Services.Implementations;

public sealed class SipsRequestSender(ILogger<SipsRequestSender> logger, IInterfaceHttpClient http)
    : ISipsRequestSender
{
    private readonly ILogger<SipsRequestSender> _logger = logger;
    private readonly IInterfaceHttpClient _http = http;

    public async Task<SIPS.ISO20022.Models.DTOs.Response<string>> SendAsync(string url, string signedXml, CancellationToken ct, string correlationId)
    {
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", correlationId, url);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", correlationId, signedXml);
        var content = new StringContent(signedXml, Encoding.UTF8, "application/xml");
        return await _http.Send4XML(url, content, ct);
    }
}
