using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SIPS.Core.Services.Persistence;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class PersistenceGatewayTests
{
    [Fact]
    public async Task Delegates_All_Calls_To_Recorder()
    {
        var recorder = new Mock<IIncomingRecorder>(MockBehavior.Strict);
        var iso = new ISOMessage();
        var status = new ISOMessageStatus();
        var ct = CancellationToken.None;

        recorder.Setup(r => r.ISOMessageAsync(iso, ct)).ReturnsAsync(iso);
        recorder.Setup(r => r.ISOMessageStatusAsync(status, ct)).ReturnsAsync(status);
        recorder.Setup(r => r.ISOMessageResponseAsync(iso, ct)).ReturnsAsync(iso);
        recorder.Setup(r => r.ISOMessageStatusResponseAsync(status, ct)).ReturnsAsync(status);
        recorder.Setup(r => r.GetISOMessageByTxIdAsync("tx", ct)).ReturnsAsync(iso);
        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync("tx", ct)).ReturnsAsync(iso);

        var gw = new PersistenceGateway(recorder.Object);

        (await gw.RecordISOMessageAsync(iso, ct)).Should().BeSameAs(iso);
        (await gw.RecordISOMessageStatusAsync(status, ct)).Should().BeSameAs(status);
        (await gw.ISOMessageResponseAsync(iso, ct)).Should().BeSameAs(iso);
        (await gw.ISOMessageStatusResponseAsync(status, ct)).Should().BeSameAs(status);
        (await gw.GetISOMessageByTxIdAsync("tx", ct)).Should().BeSameAs(iso);
        (await gw.GetISOMessageWithTransactionsByTxIdAsync("tx", ct)).Should().BeSameAs(iso);

        recorder.VerifyAll();
    }
}
