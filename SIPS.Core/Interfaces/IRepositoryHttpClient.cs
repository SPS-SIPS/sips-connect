using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Core.Interfaces;
public interface IRepositoryHttpClient
{
    Task<RepositoryResponse<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default);
    Task<RepositoryResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest content, CancellationToken cancellationToken = default);
    Task<RepositoryResponse<T>> PostEmptyAsync<T>(string url, CancellationToken cancellationToken = default);
    void AddAuthHeaders(string accessToken);
}