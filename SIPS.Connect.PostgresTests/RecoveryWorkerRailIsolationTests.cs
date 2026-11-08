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
using SIPS.PostgreSQL.Persistence;
using Xunit;

namespace SIPS.Connect.PostgresTests;

[Trait("Category", "Postgres")]
public sealed class RecoveryWorkerRailIsolationTests
{
    [Theory]
    [InlineData(TransactionStatus.Pending, 1)]
    [InlineData(TransactionStatus.CheckStatus, 1)]
    [InlineData(TransactionStatus.ReadyForReturn, 1)]
    [InlineData(TransactionStatus.CheckStatus, 10)]
    public async Task Workers_exclude_configured_papss_profile_and_select_null_service_in_postgres(TransactionStatus status, int round)
    {
        await using var harness = await PostgresHarness.CreateAsync();
        var papss = Message("PAPSS", harness.Options.SecurityProfile, status, round);
        var domestic = Message("DOMESTIC", null, status, round);
        await harness.WithStorageAsync(async db =>
        {
            db.ISOMessages.AddRange(papss, domestic);
            return await db.SaveChangesAsync(CancellationToken.None);
        });

        var outgoing = new Mock<IOutgoingTransactionStatusHandler>();
        outgoing.Setup(x => x.HandleAsync(It.IsAny<StatusRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<PaymentResponseDto>.Success(new PaymentResponseDto { Status = "PDNG" }));
        var isoService = new Mock<IISOMessageService>();
        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PapssFacing:SecurityProfile"] = harness.Options.SecurityProfile
            }).Build())
            .AddDbContext<StorageBroker>(o => o.UseNpgsql(harness.ConnectionString).UseLowerCaseNamingConvention())
            .AddScoped<IStorageBroker>(sp => sp.GetRequiredService<StorageBroker>())
            .AddSingleton(new CoreOptions { BIC = PostgresHarness.LocalBic, TransactionTimeoutMinutes = 10 })
            .AddSingleton(outgoing.Object)
            .AddSingleton(isoService.Object)
            .BuildServiceProvider();

        if (status == TransactionStatus.Pending)
        {
            using var worker = new TimeoutWorker(Schedule<TimeoutWorker>(), NullLogger<TimeoutWorker>.Instance, provider);
            await worker.DoWork(CancellationToken.None);
            var rows = await harness.WithStorageAsync(db => db.ISOMessages.AsNoTracking().OrderBy(x => x.TxId).ToListAsync());
            Assert.Equal(TransactionStatus.CheckStatus, rows.Single(x => x.TxId == domestic.TxId).Status);
            Assert.Equal(TransactionStatus.Pending, rows.Single(x => x.TxId == papss.TxId).Status);
        }
        else
        {
            using var worker = new SAFWorker(Schedule<SAFWorker>(), NullLogger<SAFWorker>.Instance, provider);
            await worker.DoWork(CancellationToken.None);
            if (round == 10)
            {
                isoService.Verify(x => x.FinalizeAfterMaxRetriesAsync(It.Is<ISOMessage>(m => m.TxId == domestic.TxId), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
                isoService.Verify(x => x.FinalizeAfterMaxRetriesAsync(It.Is<ISOMessage>(m => m.TxId == papss.TxId), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
                outgoing.VerifyNoOtherCalls();
            }
            else
            {
                outgoing.Verify(x => x.HandleAsync(It.Is<StatusRequestDto>(r => r.TxId == domestic.TxId), It.IsAny<CancellationToken>()), Times.Once);
                outgoing.Verify(x => x.HandleAsync(It.Is<StatusRequestDto>(r => r.TxId == papss.TxId), It.IsAny<CancellationToken>()), Times.Never);
                isoService.VerifyNoOtherCalls();
            }
        }
    }

    private static ISOMessage Message(string txId, string? service, TransactionStatus status, int round) => new()
    {
        TxId = txId, MsgId = txId, BizMsgIdr = txId, BusinessService = service,
        MessageType = ISOMessageType.TransactionRequest, MsgDefIdr = "pacs.008.001.10", Message = [],
        Status = status, Round = round, Date = DateTimeOffset.UtcNow.AddDays(-1),
        FromBIC = PostgresHarness.LocalBic, ToBIC = "REMOTE",
        ReturnId = status == TransactionStatus.ReadyForReturn ? $"RETURN-{txId}" : null
    };

    private static ScheduleConfig<T> Schedule<T>() => new() { CronExpression = "*/15 * * * *", TimeZoneInfo = TimeZoneInfo.Utc };
}
