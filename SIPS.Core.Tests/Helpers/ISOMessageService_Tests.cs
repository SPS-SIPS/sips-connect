using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.Core.Services.Persistence;

namespace SIPS.Core.Tests.Helpers;

public class ISOMessageService_Tests
{
    [Fact]
    public async Task PersistResponseAsync_Updates_Status_To_Success()
    {
        var pg = new Mock<IPersistenceGateway>();
        var svc = new ISOMessageService(pg.Object);
        var iso = new ISOMessage { Status = TransactionStatus.Pending };

        await svc.PersistResponseAsync(iso, TransactionStatus.Success, "Reason", null, "<rsp/>", CancellationToken.None);

        Assert.Equal(TransactionStatus.Success, iso.Status);
        Assert.Equal("Reason", iso.Reason);
        Assert.Equal(Encoding.UTF8.GetBytes("<rsp/>"), iso.Response);
        pg.Verify(p => p.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PersistStatusResponseAsync_Updates_Status_And_Parent()
    {
        var pg = new Mock<IPersistenceGateway>();
        var svc = new ISOMessageService(pg.Object);
        var parent = new ISOMessage { Status = TransactionStatus.Pending };
        var status = new ISOMessageStatus { ISOMessage = parent, Status = TransactionStatus.Pending };

        await svc.PersistStatusResponseAsync(status, TransactionStatus.Success, "", null, "<rsp/>", CancellationToken.None);

        Assert.Equal(TransactionStatus.Success, status.Status);
        Assert.Equal(TransactionStatus.Success, parent.Status);
        pg.Verify(p => p.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PersistResponseAsync_Updates_Status_To_Failed()
    {
        var pg = new Mock<IPersistenceGateway>();
        var svc = new ISOMessageService(pg.Object);
        var iso = new ISOMessage { Status = TransactionStatus.Pending };

        await svc.PersistResponseAsync(iso, TransactionStatus.Failed, "Reason", null, "<rsp/>", CancellationToken.None);

        Assert.Equal(TransactionStatus.Failed, iso.Status);
        pg.Verify(p => p.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PersistStatusResponseAsync_Maps_NonSuccess_To_Failed()
    {
        var pg = new Mock<IPersistenceGateway>();
        var svc = new ISOMessageService(pg.Object);
        var parent = new ISOMessage { Status = TransactionStatus.Pending };
        var status = new ISOMessageStatus { ISOMessage = parent, Status = TransactionStatus.Pending };

        await svc.PersistStatusResponseAsync(status, TransactionStatus.Failed, "", null, "<rsp/>", CancellationToken.None);

        Assert.Equal(TransactionStatus.Failed, status.Status);
        Assert.Equal(TransactionStatus.Failed, parent.Status);
        pg.Verify(p => p.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
