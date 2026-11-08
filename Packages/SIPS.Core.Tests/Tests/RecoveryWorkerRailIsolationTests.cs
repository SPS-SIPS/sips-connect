using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Core.Models;
using SIPS.Core.Options;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Workers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public sealed class RecoveryWorkerRailIsolationTests
{
    private const string PapssProfile = "custom-papss-profile";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Timeout_without_papss_configuration_still_selects_domestic_rows(string? profile)
    {
        var domestic = Message(1, null, TransactionStatus.Pending);
        var emptyService = Message(2, "", TransactionStatus.Pending);
        var storage = Storage(domestic, emptyService);
        using var services = Services(storage.Object, papssProfile: profile);
        using var worker = new TimeoutWorker(Schedule<TimeoutWorker>(), NullLogger<TimeoutWorker>.Instance, services);

        await worker.DoWork(CancellationToken.None);

        Assert.Equal(TransactionStatus.CheckStatus, domestic.Status);
        Assert.Equal(TransactionStatus.CheckStatus, emptyService.Status);
    }

    [Fact]
    public async Task Timeout_leaves_papss_pending_and_still_recovers_domestic_payments()
    {
        var papss = Message(1, PapssProfile, TransactionStatus.Pending);
        var domestic = Message(2, null, TransactionStatus.Pending);
        var storage = Storage(papss, domestic);
        using var services = Services(storage.Object);
        using var worker = new TimeoutWorker(Schedule<TimeoutWorker>(), NullLogger<TimeoutWorker>.Instance, services);

        await worker.DoWork(CancellationToken.None);

        Assert.Equal(TransactionStatus.Pending, papss.Status);
        Assert.Equal(1, papss.Round);
        Assert.Equal(TransactionStatus.CheckStatus, domestic.Status);
        Assert.Equal(0, domestic.Round);
    }

    [Theory]
    [InlineData(TransactionStatus.CheckStatus)]
    [InlineData(TransactionStatus.ReadyForReturn)]
    public async Task Saf_does_not_query_ips_for_papss_but_still_queries_domestic(TransactionStatus status)
    {
        var papss = Message(1, PapssProfile, status);
        var domestic = Message(2, null, status);
        var storage = Storage(papss, domestic);
        var outgoing = new Mock<IOutgoingTransactionStatusHandler>();
        outgoing.Setup(x => x.HandleAsync(It.IsAny<StatusRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<PaymentResponseDto>.Success(new PaymentResponseDto { Status = "PDNG" }));
        using var services = Services(storage.Object, outgoing.Object);
        using var worker = new SAFWorker(Schedule<SAFWorker>(), NullLogger<SAFWorker>.Instance, services);

        await worker.DoWork(CancellationToken.None);

        outgoing.Verify(x => x.HandleAsync(It.Is<StatusRequestDto>(r => r.TxId == domestic.TxId), It.IsAny<CancellationToken>()), Times.Once);
        outgoing.Verify(x => x.HandleAsync(It.Is<StatusRequestDto>(r => r.TxId == papss.TxId), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saf_does_not_finalize_papss_after_domestic_retry_exhaustion()
    {
        var papss = Message(1, PapssProfile, TransactionStatus.CheckStatus);
        var domestic = Message(2, null, TransactionStatus.CheckStatus);
        papss.Round = domestic.Round = 10;
        var storage = Storage(papss, domestic);
        var isoService = new Mock<IISOMessageService>();
        using var services = Services(storage.Object, isoService: isoService.Object);
        using var worker = new SAFWorker(Schedule<SAFWorker>(), NullLogger<SAFWorker>.Instance, services);

        await worker.DoWork(CancellationToken.None);

        isoService.Verify(x => x.FinalizeAfterMaxRetriesAsync(domestic, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        isoService.Verify(x => x.FinalizeAfterMaxRetriesAsync(papss, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ISOMessage Message(int id, string? service, TransactionStatus status) => new()
    {
        Id = id, TxId = $"TX-{id}", BusinessService = service, Status = status,
        FromBIC = "LOCAL", ToBIC = "REMOTE", ReturnId = status == TransactionStatus.ReadyForReturn ? $"RETURN-{id}" : null,
        Date = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static Mock<IStorageBroker> Storage(params ISOMessage[] messages)
    {
        IQueryable<ISOMessage> query = new TestAsyncEnumerable<ISOMessage>(messages);
        var set = new Mock<DbSet<ISOMessage>>();
        set.As<IQueryable<ISOMessage>>().Setup(x => x.Provider).Returns(((IQueryable<ISOMessage>)query).Provider);
        set.As<IQueryable<ISOMessage>>().Setup(x => x.Expression).Returns(query.Expression);
        set.As<IQueryable<ISOMessage>>().Setup(x => x.ElementType).Returns(query.ElementType);
        set.As<IQueryable<ISOMessage>>().Setup(x => x.GetEnumerator()).Returns(() => ((IEnumerable<ISOMessage>)query).GetEnumerator());
        var storage = new Mock<IStorageBroker>();
        storage.Setup(x => x.ISOMessages).Returns(set.Object);
        return storage;
    }

    private static ServiceProvider Services(IStorageBroker storage, IOutgoingTransactionStatusHandler? outgoing = null, IISOMessageService? isoService = null, string? papssProfile = PapssProfile) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PapssFacing:SecurityProfile"] = papssProfile
            }).Build())
            .AddSingleton(storage)
            .AddSingleton(new CoreOptions { BIC = "LOCAL", TransactionTimeoutMinutes = 10 })
            .AddSingleton(outgoing ?? Mock.Of<IOutgoingTransactionStatusHandler>())
            .AddSingleton(isoService ?? Mock.Of<IISOMessageService>())
            .BuildServiceProvider();

    private static ScheduleConfig<T> Schedule<T>() => new() { CronExpression = "*/15 * * * *", TimeZoneInfo = TimeZoneInfo.Utc };
}
