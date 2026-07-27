using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;

namespace SIPS.Core.Tests.Verification
{
    public class OutgoingVerificationHandlerTests
    {
        [Fact]
        public async Task HandleAsync_DoesNotOverwriteTxId_WithVerificationId()
        {
            // Arrange
            var options = new ISO20022Options { BIC = "TESTBIC", SIPS = "http://sips" };
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });
            var mockSigner = new Mock<INativeSigner>();
            var mockSignature = new Mock<ISignatureService>();
            var mockPersistence = new Mock<IPersistenceGateway>();
            var mockCorrelation = new Mock<ICorrelationService>();
            var mockSips = new Mock<ISipsRequestSender>();
            var mockStatus = new Mock<IStatusOrchestrator>();
            var mockQrCodeParser = new Mock<IQrCodeParserService>();

            // Mock correlation
            mockCorrelation.Setup(c => c.Create(It.IsAny<string?[]>())).Returns("corr-id");

            // Mock building request (returns true and generates valid signature)
            mockSigner.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns("signed-xml");

            // RecordISOMessageAsync returns the tracking object.
            var trackingRecord = new ISOMessage
            {
                TxId = "ORIGINAL_MSG_ID",
                MsgId = "ORIGINAL_MSG_ID"
            };
            mockPersistence.Setup(p => p.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(trackingRecord);

            // Mock SIPS HTTP response using actual builder
            var mockResponseData = new SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Request
            {
                From = "SIPS",
                To = "TESTBIC",
                MsgDefIdr = "acmt.024.001.03",
                MsgId = "ResMsgId",
                Verified = true,
                Reason = "SUCC",
                Address = "Washington, DC",
                Id = "401005007403",
                Type = "ACCT",
                Name = "John Doe",
                Currency = "USD",
                Original = new SIPS.ISO20022.Helpers.PayeeVerificationBuilder.Request
                {
                    SIPSRequestId = "FP",
                    MsgId = "ORIGINAL_MSG_ID", // Match trackingRecord.MsgId
                    MsgDefIdr = "acmt.023.001.03",
                    From = "TESTBIC",
                    To = "SIPS"
                }
            };
            string responseXml = SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Build(mockResponseData);
            
            mockSips.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                    .ReturnsAsync(SIPS.ISO20022.Models.DTOs.Response<string>.Success(responseXml));

            // Mock Signature verification success
            mockSignature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync((true, "OK"));

            mockStatus.Setup(s => s.MapSingleStatus(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TransactionStatus.Failed);

            var handler = new OutgoingVerificationHandler(
                options,
                NullLogger<OutgoingVerificationHandler>.Instance,
                mockSigner.Object,
                mockSignature.Object,
                mockPersistence.Object,
                mockCorrelation.Object,
                mockSips.Object,
                mockStatus.Object,
                coreOptions,
                mockQrCodeParser.Object
            );

            // Act
            var request = new VerificationRequestDto
            {
                Alias = "Alias123",
                Type = "MSISDN",
                ToBIC = "DESTBIC"
            };

            var result = await handler.HandleAsync(request, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("ORIGINAL_MSG_ID", result.Data!.SIPSRequestId); // Re-anchored to MsgId
            Assert.Equal("Washington, DC", result.Data!.Address);

            mockPersistence.Verify(p => p.ISOMessageResponseAsync(It.Is<ISOMessage>(m => 
                m.TxId == "ORIGINAL_MSG_ID" && 
                m.AdditionalInfo == "FP"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_P2GDescriptor_Uses_OriginalInvoiceOrUpr_For_Remittance()
        {
            var options = new ISO20022Options { BIC = "TESTBIC", SIPS = "http://sips" };
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });
            var mockSigner = new Mock<INativeSigner>();
            var mockSignature = new Mock<ISignatureService>();
            var mockPersistence = new Mock<IPersistenceGateway>();
            var mockCorrelation = new Mock<ICorrelationService>();
            var mockSips = new Mock<ISipsRequestSender>();
            var mockStatus = new Mock<IStatusOrchestrator>();
            var mockQrCodeParser = new Mock<IQrCodeParserService>();

            mockCorrelation.Setup(c => c.Create(It.IsAny<string?[]>())).Returns("corr-id");
            mockSigner.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns("signed-xml");
            mockPersistence.Setup(p => p.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new ISOMessage { TxId = "ORIGINAL_MSG_ID", MsgId = "ORIGINAL_MSG_ID" });

            var response = new SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Request
            {
                From = "SIPS",
                To = "TESTBIC",
                MsgDefIdr = "acmt.024.001.03",
                MsgId = "ResMsgId",
                Verified = true,
                Reason = "SUCC",
                Id = "401005007403",
                Type = "ACCT",
                Name = "P2G1|IMO-IRS-001|PAYE-2026|INV-2026-1001|USD|1500.00|20260731|TAX-123457|E58D8569",
                Address = "Imo State IRS Collection Account",
                Currency = "USD",
                Original = new SIPS.ISO20022.Helpers.PayeeVerificationBuilder.Request
                {
                    SIPSRequestId = "FP",
                    MsgId = "ORIGINAL_MSG_ID",
                    MsgDefIdr = "acmt.023.001.03",
                    From = "TESTBIC",
                    To = "SIPS"
                }
            };

            var responseXml = SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Build(response);
            mockSips.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                    .ReturnsAsync(Response<string>.Success(responseXml));
            mockSignature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync((true, "OK"));
            mockStatus.Setup(s => s.MapSingleStatus(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TransactionStatus.Success);

            var handler = new OutgoingVerificationHandler(
                options,
                NullLogger<OutgoingVerificationHandler>.Instance,
                mockSigner.Object,
                mockSignature.Object,
                mockPersistence.Object,
                mockCorrelation.Object,
                mockSips.Object,
                mockStatus.Object,
                coreOptions,
                mockQrCodeParser.Object
            );

            var result = await handler.HandleAsync(new VerificationRequestDto
            {
                Alias = "UPR-ORIGINAL-999",
                Type = "UPR",
                ToBIC = "DESTBIC"
            }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Data!.IsP2G);
            Assert.Equal("UPR-ORIGINAL-999", result.Data.BillReference);
            Assert.Equal("BILL:UPR-ORIGINAL-999", result.Data.RemittanceInformation);
            Assert.Equal("INV-2026-1001", result.Data.InvoiceId);
            Assert.Equal(1500.00m, result.Data.AmountPayable);
            Assert.True(result.Data.AmountLocked);
        }

        [Fact]
        public async Task HandleAsync_P2GDescriptor_Rejects_NonBillLookup()
        {
            var options = new ISO20022Options { BIC = "TESTBIC", SIPS = "http://sips" };
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });
            var mockSigner = new Mock<INativeSigner>();
            var mockSignature = new Mock<ISignatureService>();
            var mockPersistence = new Mock<IPersistenceGateway>();
            var mockCorrelation = new Mock<ICorrelationService>();
            var mockSips = new Mock<ISipsRequestSender>();
            var mockStatus = new Mock<IStatusOrchestrator>();
            var mockQrCodeParser = new Mock<IQrCodeParserService>();

            mockCorrelation.Setup(c => c.Create(It.IsAny<string?[]>())).Returns("corr-id");
            mockSigner.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns("signed-xml");
            mockPersistence.Setup(p => p.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new ISOMessage { TxId = "ORIGINAL_MSG_ID", MsgId = "ORIGINAL_MSG_ID" });

            var response = new SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Request
            {
                From = "SIPS",
                To = "TESTBIC",
                MsgDefIdr = "acmt.024.001.03",
                MsgId = "ResMsgId",
                Verified = true,
                Reason = "SUCC",
                Id = "401005007403",
                Type = "ACCT",
                Name = "P2G1|IMO-IRS-001|PAYE-2026|INV-2026-1001|USD|1500.00|20260731|TAX-123457|E58D8569",
                Address = "Imo State IRS Collection Account",
                Currency = "USD",
                Original = new SIPS.ISO20022.Helpers.PayeeVerificationBuilder.Request
                {
                    SIPSRequestId = "FP",
                    MsgId = "ORIGINAL_MSG_ID",
                    MsgDefIdr = "acmt.023.001.03",
                    From = "TESTBIC",
                    To = "SIPS"
                }
            };

            var responseXml = SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Build(response);
            mockSips.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                    .ReturnsAsync(Response<string>.Success(responseXml));
            mockSignature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync((true, "OK"));
            mockStatus.Setup(s => s.MapSingleStatus(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(TransactionStatus.Success);

            var handler = new OutgoingVerificationHandler(
                options,
                NullLogger<OutgoingVerificationHandler>.Instance,
                mockSigner.Object,
                mockSignature.Object,
                mockPersistence.Object,
                mockCorrelation.Object,
                mockSips.Object,
                mockStatus.Object,
                coreOptions,
                mockQrCodeParser.Object
            );

            var result = await handler.HandleAsync(new VerificationRequestDto
            {
                Alias = "401005007403",
                Type = "ACCT",
                ToBIC = "DESTBIC"
            }, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("P2G descriptor requires original BILL/INVOICE/UPR lookup input", result.Message);
        }
    }
}
