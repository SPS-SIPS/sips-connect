using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingVerificationHandler_Tests
{
    private static (IncomingVerificationHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter)
        CreateSut(Func<Response<JsonObject?>> httpResultFactory)
    {
        var options = new ISO20022Options
        {
            Verification = "https://example.test/verification",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingVerificationHandler>>();

        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(v => v.VerifySignature(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, new VerboseResult { SignatureStatus = "ok" }));

        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => httpResultFactory());

        var signer = Mock.Of<INativeSigner>(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()) == "signed");

        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.ToObject<VerificationResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new VerificationResponseDto { IsVerified = true, Name = "John", Id = "ID1", Currency = "USD" });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);

        var parser = new SIPS.Core.Tests.Parsers.PayeeVerificationRequestParserShim();

        var sut = new IncomingVerificationHandler(options, logger, http.Object, signer, verifier.Object, adapter.Object, recorder.Object, parser);
        return (sut, recorder, http, adapter);
    }

    private static string LoadFixture()
        => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "acmt.023.xml"));

    [Fact]
    public async Task HandleAsync_ReturnsSignedEnvelope_AndPersistsSuccess()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Success), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpIsNotOk()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.BadGateway, Message = "bad" });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpOkButNullData()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.OK, Message = "null" });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }
}
