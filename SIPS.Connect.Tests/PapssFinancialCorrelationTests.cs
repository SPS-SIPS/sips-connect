using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class PapssFinancialCorrelationTests
{
    [Fact]
    public async Task Fully_correlated_signed_technical_admission_is_accepted_without_business_semantics()
    {
        var client = Client(wrongMessage: false);
        var response = await client.PayAsync(new("bank-a", "BANKSOSIXXX", "bank-a", null), Payment(), CancellationToken.None);
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response.Code);
        Assert.True(response.DurablyAdmitted);
    }

    [Fact]
    public async Task Cryptographically_valid_response_for_another_message_is_rejected()
    {
        var participant = new PapssParticipantBinding("bank-a", "BANKSOSIXXX", "bank-a", null);
        var client = Client(wrongMessage: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PayAsync(participant, Payment(), CancellationToken.None));
    }

    [Fact]
    public async Task Unconfigured_corridor_is_rejected_before_network()
    {
        var payment = Payment(); payment.ReceiverCountry = "ZZ";
        await Assert.ThrowsAsync<ArgumentException>(() => Client(false).PayAsync(new("bank-a", "BANKSOSIXXX", "bank-a", null), payment, CancellationToken.None));
    }

    [Fact]
    public async Task Debtor_agent_must_match_authenticated_participant_bic()
    {
        var payment = Payment(); payment.DebtorAgentBIC = "OTHERBANKXXX";
        var error = await Assert.ThrowsAsync<ParticipantRailException>(() => Client(false).PayAsync(new("bank-a", "BANKSOSIXXX", "bank-a", null), payment, CancellationToken.None));
        Assert.Equal("PARTICIPANT_BIC_MISMATCH", error.Code);
    }

    private static PapssFacingSipsClient Client(bool wrongMessage)
    {
        var options = Options();
        var signer = new Mock<INativeSigner>(); signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<string>())).Returns<string, XadesProfile, string>((xml, _, _) => xml);
        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(x => x.VerifyWithProvenance(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<CancellationToken>())).ReturnsAsync(new SignatureVerificationResult(true, new VerboseResult(),
            new("PAPSS", "CA", "test", "PAPSS", "issuer", "1", "hash", true, "v1", "admi.002.001.01", options.SecurityProfile, "hash", DateTimeOffset.UtcNow)));
        var http = new HttpClient(new ResponseHandler(requestXml => Response(requestXml, wrongMessage)));
        return new PapssFacingSipsClient(options, http, signer.Object, verifier.Object, new PapssHealthState(), NullLogger<PapssFacingSipsClient>.Instance);
    }

    private static string Response(string requestXml, bool wrongMessage)
    {
        if (wrongMessage)
        {
            var request = XDocument.Parse(requestXml);
            request.Descendants().Single(x => x.Name.LocalName == "BizMsgIdr").Value = "ANOTHER-MESSAGE";
            requestXml = request.ToString(SaveOptions.DisableFormatting);
        }
        return AdminMessageBuilder.BuildForRejectedEnvelope(requestXml, "PAPSS-ADMISSION-1", DateTimeOffset.UtcNow,
            "RECEIVED_AND_DURABLY_ADMITTED", description: "Technical admission only.");
    }

    private static PaymentRequestDto Payment() => new() { Rail="PAPSS", ToBIC="BANKKE00XXX", LocalInstrument="INST", CategoryPurpose="CASH", EndToEndId="E2E-1", TxId="TX-1", Amount=10, Currency="SOS", SenderCountry="SO", ReceiverCountry="KE", SenderCurrency="SOS", ReceiverCurrency="KES", DebtorName="Debtor", DebtorAccount="D1", DebtorAccountType="ACCT", DebtorAgentBIC="BANKSOSIXXX", CreditorName="Creditor", CreditorAccount="C1", CreditorAccountType="ACCT", CreditorAgentBIC="BANKKE00XXX", RemittanceInformation="test" };
    private static PapssFacingOptions Options() => new() { Enabled=true, IsoIngressUrl="https://papss.test/sips/messages", AllowedHosts=["papss.test"], AllowedLocalInstruments=["INST"], AllowedCorridors=[new() { SenderCountry="SO", ReceiverCountry="KE", SenderCurrency="SOS", ReceiverCurrency="KES", DestinationBic="BANKKE00XXX", LocalInstruments=["INST"] }], RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001", RequestTimeoutSeconds=5 };

    private sealed class ResponseHandler(Func<string, string> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(HttpStatusCode.OK) { Content = new StringContent(response(await request.Content!.ReadAsStringAsync(cancellationToken)), Encoding.UTF8, "application/xml") };
    }
}
