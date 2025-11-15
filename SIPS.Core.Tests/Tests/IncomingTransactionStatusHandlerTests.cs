using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingTransactionStatusHandlerTests
{
    private static IncomingTransactionStatusHandler CreateSut(
        Mock<ISignatureService>? signatureMock = null,
        Mock<IPaymentStatusRequestParser>? parserMock = null,
        Mock<IInterfaceHttpClient>? httpClientMock = null,
        Mock<IIncomingRecorder>? recorderMock = null
    )
    {
        var options = new ISO20022Options
        {
            Status = "https://example.test/status",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingTransactionStatusHandler>>();
        var signer = Mock.Of<INativeSigner>();
        var jsonAdapter = Mock.Of<IJsonAdapter>();
        var recorder = (recorderMock ?? new Mock<IIncomingRecorder>()).Object;

        var httpClient = (httpClientMock ?? new Mock<IInterfaceHttpClient>()).Object;
        var verifier = new Mock<INativeVerifier>().Object; // no longer used directly

        // New dependencies
        var correlation = new CorrelationService();
        var responses = new ResponseFactory();
        var persistence = new PersistenceGateway(recorder);
        var cbLogger = Mock.Of<ILogger<CallbackClient>>();
        var callback = new CallbackClient(httpClient, cbLogger, correlation);
    var callbacks = new CallbackOrchestrator();
    var isoService = new ISOMessageService(persistence);
    var signature = (signatureMock ?? new Mock<ISignatureService>()).Object;
    var inbound = new InboundMessageService(signature);
    var parser = (parserMock ?? new Mock<IPaymentStatusRequestParser>()).Object;
        var statusOrchestrator = new Mock<IStatusOrchestrator>().Object;
        var coreOptions = Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions());

        return new IncomingTransactionStatusHandler(options, logger, httpClient, signer, verifier, jsonAdapter, recorder,
            signature, parser, callback, responses, persistence, correlation, inbound, callbacks, isoService, statusOrchestrator, coreOptions);
    }

    [Fact]
    public async Task HandleAsync_ReturnsError_WhenSignatureVerificationFails()
    {
        // Arrange
        var signature = new Mock<ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((false, "bad-signature"));
        var sut = CreateSut(signatureMock: signature);

        // Act
        var result = await sut.HandleAsync("<xml>payload</xml>", CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Failed to verify the signature");
    }

    [Fact]
    public async Task HandleAsync_ReturnsError_WhenParsingFails()
    {
        // Arrange
        var signature = new Mock<ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
        var parser = new Mock<IPaymentStatusRequestParser>();
        parser.Setup(p => p.TryParse(It.IsAny<string>(), out It.Ref<SIPS.ISO20022.Helpers.PaymentStatusRequestBuilder.Request>.IsAny))
              .Returns(false);
        var sut = CreateSut(signatureMock: signature, parserMock: parser);

        // Act
        var result = await sut.HandleAsync("not-a-valid-iso20022-message", CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Failed to verify the signature or parse the message");
    }
}
