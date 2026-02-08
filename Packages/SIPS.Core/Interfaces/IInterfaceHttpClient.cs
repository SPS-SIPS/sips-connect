using System.Net;
using System.Text.Json.Nodes;
using SIPS.ISO20022.Models.DTOs;
namespace SIPS.Core.Interfaces;
public interface IInterfaceHttpClient
{
    Task<Response<JsonObject?>> Send(string completeUrl, Dictionary<string, string>? headers, StringContent content, CancellationToken cancellationToken = default);
    Task<Response<string>> Send4XML(string url, StringContent content, CancellationToken cancellationToken = default);
    void AddAuthHeaders(string accessToken);
}