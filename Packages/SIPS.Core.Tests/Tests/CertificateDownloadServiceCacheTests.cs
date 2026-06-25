using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class CertificateDownloadServiceCacheTests
{
    [Fact]
    public async Task GetCertificatesAsync_UsesNormalizedIssuerDnForCacheKey()
    {
        var cache = new InMemoryCacheService();
        var auth = new Mock<IAuthService>();
        var http = new Mock<IRepositoryHttpClient>();
        var certificate = new CertificateDownloadResponse("CERT", "OWNER");

        auth.Setup(x => x.LoginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((new LoginResponse("token", Guid.NewGuid(), 3600, 7200), null));

        http.Setup(x => x.PostAsync<CertificateRequest, CertificateDownloadResponse>(
                "https://repo/certs",
                It.IsAny<CertificateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RepositoryResponse<CertificateDownloadResponse>.Success(certificate, 60));

        var service = new CertificateDownloadService(
            new CoreOptions { PublicKeysRepUrl = "https://repo/certs" },
            NullLogger<CertificateDownloadService>.Instance,
            http.Object,
            auth.Object,
            cache);

        var first = await service.GetCertificatesAsync(
            "1159638750646433804973129259982911450457833534",
            "CN=ISSUEING-CA, DC=int, DC=sips, DC=prd");
        var second = await service.GetCertificatesAsync(
            "1159638750646433804973129259982911450457833534",
            "CN=ISSUEING-CA,DC=int,DC=sips,DC=prd");

        first.Error.Should().BeNull();
        second.Error.Should().BeNull();
        second.Certificates.Should().Be(certificate);
        http.Verify(x => x.PostAsync<CertificateRequest, CertificateDownloadResponse>(
            "https://repo/certs",
            It.IsAny<CertificateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, object> _cache = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken token = default)
        {
            return Task.FromResult(_cache.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
        {
            _cache[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _cache.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
