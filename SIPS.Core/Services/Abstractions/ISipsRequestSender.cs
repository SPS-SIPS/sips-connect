using System.Threading;
using System.Threading.Tasks;

namespace SIPS.Core.Services.Abstractions;

public interface ISipsRequestSender
{
    Task<SIPS.ISO20022.Models.DTOs.Response<string>> SendAsync(string url, string signedXml, CancellationToken ct, string correlationId);
}
