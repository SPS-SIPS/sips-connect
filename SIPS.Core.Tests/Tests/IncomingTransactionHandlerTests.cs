using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Options;

namespace SIPS.Core.Tests.Tests;

public class IncomingTransactionHandlerTests
{
    private static IncomingTransactionHandler CreateSut(
        Mock<INativeVerifier>? verifierMock = null,
        Mock<IInterfaceHttpClient>? httpClientMock = null,
        Mock<ISignatureService>? signatureMock = null,
        Mock<IPaymentRequestParser>? parserMock = null
    )
    {
        var options = new ISO20022Options
        {
            Transfer = "https://example.test/transfer",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingTransactionHandler>>();
        var signerMock = new Mock<INativeSigner>();
        signerMock.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns((string message, string _) => message);
        var signer = signerMock.Object;
        var jsonAdapter = Mock.Of<IJsonAdapter>();
        var recorderMock = new Mock<SIPS.PostgreSQL.Interfaces.IIncomingRecorder>();
        var recorder = recorderMock.Object;

        var httpClient = (httpClientMock ?? new Mock<IInterfaceHttpClient>()).Object;
        var verifier = (verifierMock ?? new Mock<INativeVerifier>()).Object;

        // New dependencies
        var correlation = new CorrelationService();
        var responses = new ResponseFactory();
        var persistence = new PersistenceGateway(recorderMock.Object);

    var httpClientForCb = httpClientMock ?? new Mock<IInterfaceHttpClient>();
    var cbLogger = Mock.Of<ILogger<CallbackClient>>();
    var callback = new CallbackClient(httpClientForCb.Object, cbLogger, correlation);
    var callbacks = new CallbackOrchestrator();

        var signature = (signatureMock ?? new Mock<ISignatureService>()).Object;
        var parser = (parserMock ?? new Mock<IPaymentRequestParser>()).Object;
        var inbound = new InboundMessageService(signature);
    var isoMessageService = new ISOMessageService(persistence);
        var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions());
        // SIPS.Core.Services.Abstractions.IInboundMessageService
        return new IncomingTransactionHandler(options, logger, httpClient, signer, verifier, jsonAdapter, recorder,
            signature, parser, callback, responses, persistence, correlation, inbound, callbacks, isoMessageService, coreOptions);
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
        var parser = new Mock<IPaymentRequestParser>();
        parser.Setup(p => p.TryParse(It.IsAny<string>(), out It.Ref<SIPS.ISO20022.Helpers.PaymentRequestBuilder.Request>.IsAny))
              .Returns(false);
        var sut = CreateSut(signatureMock: signature, parserMock: parser);

        // Act
        var result = await sut.HandleAsync("not-a-valid-iso20022-message", CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Failed to verify the signature or parse the message");
    }
}
