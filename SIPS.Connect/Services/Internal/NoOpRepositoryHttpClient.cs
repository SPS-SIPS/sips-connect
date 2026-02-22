using SIPS.Core.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Connect.Services.Internal;

public class NoOpRepositoryHttpClient : IRepositoryHttpClient
{
    public Task<RepositoryResponse<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RepositoryResponse<T>.Fail("Request skipped: Gateway is running in PKI-Off (Reviewer) mode.", 400));
    }

    public Task<RepositoryResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest content, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RepositoryResponse<TResponse>.Fail("Request skipped: Gateway is running in PKI-Off (Reviewer) mode.", 400));
    }

    public Task<RepositoryResponse<T>> PostEmptyAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RepositoryResponse<T>.Fail("Request skipped: Gateway is running in PKI-Off (Reviewer) mode.", 400));
    }

    public void AddAuthHeaders(string accessToken)
    {
        // No-op
    }
}
