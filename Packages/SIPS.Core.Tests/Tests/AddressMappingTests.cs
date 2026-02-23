using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Moq;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.Adapter;
using SIPS.ISO20022.Models;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Interfaces;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.ISO20022.Options;
using SIPS.ISO20022.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class AddressMappingTests
{
    private static PaymentRequestDto CreateValidDto() => new PaymentRequestDto
    {
        TxId = "TX123",
        EndToEndId = "E2E123",
        Amount = 100,
        Currency = "USD",
        ToBIC = "TOBIC",
        LocalInstrument = "LI",
        CategoryPurpose = "CP",
        DebtorName = "Debtor",
        DebtorAccount = "DA",
        DebtorAccountType = "IBAN",
        DebtorAddress = "Debtor Street 1",
        CreditorName = "Creditor",
        CreditorAccount = "CA",
        CreditorAccountType = "IBAN",
        CreditorAddress = "Creditor Ave 2",
        CreditorAgentBIC = "CBIC"
    };

    private static ISO20022Options CreateValidOptions() => new ISO20022Options
    {
        SIPS = "http://localhost",
        BIC = "TESTBIC",
        Agent = "TESTAGENT"
    };

    [Fact]
    public async Task ISOMessageService_RecordIncomingTransactionAsync_MapsBothAddresses()
    {
        // Arrange
        var recorder = new Mock<IIncomingRecorder>();
        ISOMessage? capturedMessage = null;
        recorder.Setup(r => r.TryRecordIncomingTransactionAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ISOMessage, CancellationToken>((m, _) => capturedMessage = m)
                .ReturnsAsync((capturedMessage, true));

        var persistence = new PersistenceGateway(recorder.Object);
        var sut = new ISOMessageService(persistence);

        var request = new PaymentRequestBuilder.Request
        {
            Debtor = new Person { Name = "D", Account = "DA", Address = "Debtor Street 1" },
            Creditor = new Person { Name = "C", Account = "CA", Address = "Creditor Ave 2" },
            Amount = 100,
            Currency = "USD",
            TxId = "TX123",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "DEF",
            MsgId = "MSG"
        };

        // Act
        await sut.TryRecordIncomingTransactionAsync(request, "raw-xml", CancellationToken.None);

        // Assert
        capturedMessage.Should().NotBeNull();
        var tx = capturedMessage!.Transactions.First();
        tx.DebtorAddress.Should().Be("Debtor Street 1");
        tx.CreditorAddress.Should().Be("Creditor Ave 2");
    }

    [Fact]
    public async Task OutgoingTransactionHandler_HandleAsync_MapsBothAddresses()
    {
        // Arrange
        var message = CreateValidDto();
        string? capturedXml = null;

        var recorder = new Mock<IIncomingRecorder>();
        ISOMessage? capturedEntity = null;
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ISOMessage, CancellationToken>((m, _) => capturedEntity = m)
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);

        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
              .Callback<string, string>((xml, _) => capturedXml = xml)
              .Returns("signed");

        var sipsSender = new Mock<ISipsRequestSender>();
        sipsSender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                  .ReturnsAsync(new SIPS.ISO20022.Models.DTOs.Response<string>("ok"));

        var sut = new OutgoingTransactionHandler(
            CreateValidOptions(),
            Mock.Of<ILogger<OutgoingTransactionHandler>>(),
            signer.Object,
            Mock.Of<ISignatureService>(),
            new PersistenceGateway(recorder.Object),
            new CorrelationService(),
            sipsSender.Object,
            Mock.Of<IISOMessageService>(),
            Mock.Of<IStatusOrchestrator>(),
            Microsoft.Extensions.Options.Options.Create(new CoreOptions()));

        // Act
        await sut.HandleAsync(message, CancellationToken.None);

        // Assert
        // Verify XML mapping
        capturedXml.Should().Contain("Debtor Street 1");
        capturedXml.Should().Contain("Creditor Ave 2");

        // Verify Entity mapping
        capturedEntity.Should().NotBeNull();
        var tx = capturedEntity!.Transactions.First();
        tx.DebtorAddress.Should().Be("Debtor Street 1");
        tx.CreditorAddress.Should().Be("Creditor Ave 2");
    }

    [Fact]
    public async Task IncomingTransactionStatusHandler_HandleAsync_MapsBothAddresses()
    {
        // Arrange
        var xml = @"<AppHdr xmlns=""urn:iso:std:iso:20022:tech:xsd:head.001.001.01""><Fr><FIId><FinInstnId><Othr><Id>FROM</Id></Othr></FinInstnId></FIId></Fr><To><FIId><FinInstnId><Othr><Id>TO</Id></Othr></FinInstnId></FIId></To><BizMsgIdr>BIZ</BizMsgIdr><MsgDefIdr>pacs.028</MsgDefIdr><CreDt>2026-02-23T10:00:00Z</CreDt></AppHdr>";
        
        var req = new PaymentStatusRequestBuilder.Request { OrgnlTxId = "TX123", MsgId = "MSG", From = "FROM", To = "TO", MsgDefIdr = "pacs.028", BizMsgIdr = "BIZ", CreDt = DateTime.UtcNow };

        var inbound = new Mock<IInboundMessageService>();
        inbound.Setup(i => i.VerifyAndParseAsync(It.IsAny<string>(), It.IsAny<Func<string, (bool, PaymentStatusRequestBuilder.Request?)>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
               .ReturnsAsync((true, req));

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessage 
                { 
                    TxId = "TX123", 
                    Status = TransactionStatus.Success,
                    Transactions = new List<Transaction> 
                    { 
                        new Transaction 
                        { 
                            DebtorName = "Debtor Name",
                            DebtorAddress = "Debtor Street 1", 
                            CreditorName = "Creditor Name",
                            CreditorAddress = "Creditor Ave 2" 
                        } 
                    } 
                });

        var signer = new Mock<INativeSigner>();
        string? resultXml = null;
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
              .Callback<string, string>((msg, _) => resultXml = msg)
              .Returns("signed_envelope");

        var isoService = new Mock<IISOMessageService>();
        isoService.Setup(i => i.RecordIncomingStatusAsync(It.IsAny<ISOMessage>(), It.IsAny<string>(), It.IsAny<SIPS.ISO20022.Enums.Pacs002Role>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ISOMessageStatus());

        var sut = new IncomingTransactionStatusHandler(
            CreateValidOptions(),
            Mock.Of<ILogger<IncomingTransactionStatusHandler>>(),
            Mock.Of<IInterfaceHttpClient>(),
            signer.Object,
            Mock.Of<INativeVerifier>(),
            Mock.Of<IJsonAdapter>(),
            recorder.Object,
            Mock.Of<ISignatureService>(),
            Mock.Of<IPaymentStatusRequestParser>(),
            Mock.Of<ICallbackClient>(),
            new ResponseFactory(),
            new PersistenceGateway(recorder.Object),
            new CorrelationService(),
            inbound.Object,
            Mock.Of<ICallbackOrchestrator>(),
            isoService.Object,
            Mock.Of<IStatusOrchestrator>(),
            Microsoft.Extensions.Options.Options.Create(new CoreOptions())
        );

        // Act
        var finalXml = await sut.HandleAsync(xml, CancellationToken.None);

        // Assert
        finalXml.Should().Be("signed_envelope");
        resultXml.Should().NotBeNullOrEmpty("The signer should have been called with the response XML");
        resultXml.Should().Contain("Debtor Street 1");
        resultXml.Should().Contain("Creditor Ave 2");
        resultXml.Should().Contain("Debtor Name");
        resultXml.Should().Contain("Creditor Name");
    }
}
