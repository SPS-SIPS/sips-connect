using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Enums;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Models.WpSips;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class PapssFinancialCorrelationTests
{
    [Fact]
    public async Task Payment_derives_remote_corridor_from_signed_directory_observations()
    {
        var directory = new DirectoryScenario();
        var response = await Client(directory).PayAsync(Binding(), Payment(), CancellationToken.None);
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response.Code);
        Assert.Equal(3, directory.Requests.Count);
        Assert.All(directory.Requests, x => Assert.Equal("/sips/messages", x.Path));
        Assert.Equal("SO", Corridor(directory, "SenderCountry"));
        Assert.Equal("KE", Corridor(directory, "ReceiverCountry"));
        Assert.Equal("SOS", Corridor(directory, "SenderCurrency"));
        Assert.Equal("KES", Corridor(directory, "ReceiverCurrency"));
    }

    [Theory]
    [InlineData("sender-country")]
    [InlineData("sender-currency")]
    [InlineData("receiver-country")]
    public async Task Legacy_authority_fields_may_not_contradict_authoritative_values(string field)
    {
        var payment = Payment();
        if (field == "sender-country") payment.SenderCountry = "UG";
        if (field == "sender-currency") payment.SenderCurrency = "USD";
        if (field == "receiver-country") payment.ReceiverCountry = "UG";
        var error = await Assert.ThrowsAsync<ParticipantRailException>(() => Client(new()).PayAsync(Binding(), payment, CancellationToken.None));
        Assert.Equal("PARTICIPANT_AUTHORITY_MISMATCH", error.Code);
    }

    [Fact]
    public async Task Bank_supplied_purpose_code_is_carried_as_Purp_Cd_and_never_defaulted()
    {
        var directory = new DirectoryScenario();
        var payment = Payment(); payment.PurposeCode = "gdds";
        await Client(directory).PayAsync(Binding(), payment, CancellationToken.None);
        var purp = XDocument.Parse(directory.FinancialRequest!).Descendants().Single(x => x.Name.LocalName == "Purp");
        Assert.Equal("GDDS", purp.Elements().Single(x => x.Name.LocalName == "Cd").Value);

        var withoutPurpose = new DirectoryScenario();
        await Client(withoutPurpose).PayAsync(Binding(), Payment(), CancellationToken.None);
        Assert.Empty(XDocument.Parse(withoutPurpose.FinancialRequest!).Descendants().Where(x => x.Name.LocalName == "Purp"));
    }

    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("A-1")]
    public async Task Purpose_code_must_have_the_ISO_code_shape(string code)
    {
        var payment = Payment(); payment.PurposeCode = code;
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new DirectoryScenario()).PayAsync(Binding(), payment, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_debtor_agent_defaults_to_the_authenticated_participant_bic()
    {
        // No PaymentRequest jsonAdapter mapping carries a debtor agent (UAT probe was refused with PARTICIPANT_BIC_MISMATCH).
        var directory = new DirectoryScenario();
        var payment = Payment(); payment.DebtorAgentBIC = null!;
        var response = await Client(directory).PayAsync(Binding(), payment, CancellationToken.None);
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response.Code);
        Assert.Equal(Binding().Bic, payment.DebtorAgentBIC);
    }

    [Fact]
    public async Task Debtor_agent_must_match_authenticated_participant_bic()
    {
        var payment = Payment(); payment.DebtorAgentBIC = "OTHERBANKXXX";
        var error = await Assert.ThrowsAsync<ParticipantRailException>(() => Client(new()).PayAsync(Binding(), payment, CancellationToken.None));
        Assert.Equal("PARTICIPANT_BIC_MISMATCH", error.Code);
    }

    [Fact]
    public async Task Creditor_agent_must_match_transaction_destination_bic()
    {
        var payment = Payment(); payment.CreditorAgentBIC = "OTHERBANKXXX";
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new()).PayAsync(Binding(), payment, CancellationToken.None));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    [InlineData("suspended")]
    [InlineData("offline")]
    [InlineData("currency")]
    [InlineData("instrument")]
    public async Task Ineligible_or_unsupported_directory_observations_fail_closed(string condition)
    {
        var directory = new DirectoryScenario(); var payment = Payment();
        if (condition == "missing") directory.DiscoveryCount = 0;
        if (condition == "duplicate") directory.DiscoveryCount = 2;
        if (condition == "stale") directory.ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        if (condition == "suspended") directory.Status = "SUSPENDED";
        if (condition == "offline") directory.Online = false;
        if (condition == "currency") payment.ReceiverCurrency = "USD";
        if (condition == "instrument") payment.LocalInstrument = "RTGS";
        await Assert.ThrowsAnyAsync<Exception>(() => Client(directory).PayAsync(Binding(), payment, CancellationToken.None));
        Assert.Null(directory.FinancialRequest);
    }

    [Fact]
    public async Task Directory_changes_apply_without_redeployment_and_sps_policy_remains_independent()
    {
        var directory = new DirectoryScenario(); var client = Client(directory);
        await client.PayAsync(Binding(), Payment(), CancellationToken.None);
        directory.Country = "UG"; directory.Currencies = ["UGX"]; directory.FinancialRequest = null;
        var changed = Payment(); changed.ReceiverCountry = null; changed.ReceiverCurrency = "UGX";
        await client.PayAsync(Binding(), changed, CancellationToken.None);
        Assert.Equal("UG", Corridor(directory, "ReceiverCountry"));
        var restricted = Options(); restricted.SpsPolicy.AllowedLocalInstruments = ["RTGS"];
        await Assert.ThrowsAsync<ArgumentException>(() => Client(directory, restricted).PayAsync(Binding(), changed, CancellationToken.None));
    }

    [Fact]
    public async Task Usd_payment_instrument_is_recognized_from_the_papss_payment_schema()
    {
        var directory = new DirectoryScenario
        {
            Country = "SO",
            Currencies = ["USD"],
            PaymentSchemas = ["PS-SO-USD-USD-SSA_3-USDP"]
        };
        var payment = Payment();
        payment.Currency = "USD";
        payment.SenderCurrency = "USD";
        payment.ReceiverCurrency = "USD";
        payment.ReceiverCountry = "SO";
        payment.LocalInstrument = "USDP";

        var response = await Client(directory).PayAsync(Binding() with { SendingCurrencies = ["USD"] }, payment, CancellationToken.None);

        Assert.True(response.DurablyAdmitted);
        var localInstrument = XDocument.Parse(directory.FinancialRequest!).Descendants().Single(x => x.Name.LocalName == "LclInstrm");
        Assert.Equal("USDP", localInstrument.Descendants().Single(x => x.Name.LocalName == "Cd").Value);
    }

    [Fact]
    public async Task Lcy_payment_instrument_is_recognized_from_the_papss_payment_schema()
    {
        var directory = new DirectoryScenario { PaymentSchemas = ["PS-KE-KES-KE0001NP-KE-KES-SSA_3-DEF-SSA3"] };
        var payment = Payment(); payment.LocalInstrument = "SSA3";

        var response = await Client(directory).PayAsync(Binding(), payment, CancellationToken.None);

        Assert.True(response.DurablyAdmitted);
    }

    [Fact]
    public async Task Internal_ssa_3_schema_segment_does_not_authorize_ssa3_for_usd_payments()
    {
        var directory = new DirectoryScenario
        {
            Country = "SO",
            Currencies = ["USD"],
            PaymentSchemas = ["PS-SO-USD-USD-SSA_3-USDP"]
        };
        var payment = Payment();
        payment.Currency = "USD";
        payment.SenderCurrency = "USD";
        payment.ReceiverCurrency = "USD";
        payment.ReceiverCountry = "SO";
        payment.LocalInstrument = "SSA3";

        await Assert.ThrowsAsync<ArgumentException>(() => Client(directory).PayAsync(Binding() with { SendingCurrencies = ["USD"] }, payment, CancellationToken.None));
        Assert.Null(directory.FinancialRequest);
    }

    [Fact]
    public async Task Cryptographically_valid_admission_for_another_message_is_rejected()
    {
        var directory = new DirectoryScenario { WrongAdmissionCorrelation = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => Client(directory).PayAsync(Binding(), Payment(), CancellationToken.None));
    }

    [Fact]
    public async Task Signed_admission_rejection_preserves_the_remote_reason_for_diagnostics()
    {
        var directory = new DirectoryScenario
        {
            AdmissionCode = "AUTHORIZATION_REJECTED",
            AdmissionDescription = "The signer does not represent the BAH sender."
        };

        var error = await Assert.ThrowsAsync<ParticipantRailException>(() =>
            Client(directory).VerifyAsync(Binding(), new() { ToBIC="BANKKE00XXX", Alias="A1", Type="BBAN" }, CancellationToken.None));

        Assert.Equal("AUTHORIZATION_REJECTED", error.Code);
        Assert.Contains(directory.AdmissionDescription, error.Message);
    }

    [Fact]
    public async Task Usd_to_local_currency_payment_fails_closed_while_fx_is_indicative()
    {
        var directory = new DirectoryScenario { Currencies = ["KES"] };
        var payment = Payment(); payment.Currency = "USD"; payment.SenderCurrency = "USD";
        var binding = Binding() with { SendingCurrencies = ["SOS", "USD"] };
        var error = await Assert.ThrowsAsync<ParticipantRailException>(() => Client(directory).PayAsync(binding, payment, CancellationToken.None));
        Assert.Equal("PAPSS_FX_FEE_NOT_READY", error.Code);
        Assert.Null(directory.FinancialRequest);
    }

    [Fact]
    public async Task Payment_decision_uses_common_ingress_and_accepts_only_technical_admission()
    {
        var directory = new DirectoryScenario();
        var original = PaymentRequestBuilder.Build(new()
        {
            From="PAPSS", To="BANKSOSIXXX", MsgId="MSG-1", BizMsgIdr="BAH-1", MsgDefIdr="pacs.008.001.10", CreDt=DateTime.UtcNow,
            InstrId="INSTR-1", EndToEndId="E2E-1", TxId="TX-1", UETR=Guid.NewGuid().ToString(), Amount=10, Currency="SOS",
            LocalInstrument="INST", CategoryPurpose="CASH", Ustrd="test", Debtor=new() { Name="D", Account="D1", Address="A", AccountType="ACCT", AgentBIC="PAPSS", Issuer="I" },
            Creditor=new() { Name="C", Account="C1", Address="A", AccountType="ACCT", AgentBIC="BANKSOSIXXX", Issuer="I" }
        }).document;
        var parsed = PaymentRequestBuilder.Parse(original)!;
        var decision = XDocument.Parse(PaymentRequestResponseBuilder.Build(new() { From="BANKSOSIXXX", To="PAPSS", Original=parsed, Status="ACCP" }));
        var header = decision.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").AddAfterSelf(new XElement(header.Name.Namespace + "BizSvc", Options().SecurityProfile));

        var admission = await Client(directory).SubmitPaymentDecisionAsync(Binding(), decision.ToString(SaveOptions.DisableFormatting), CancellationToken.None);

        Assert.True(admission.DurablyAdmitted);
        Assert.Equal("/sips/messages", directory.Requests.Single().Path);
    }

    [Fact]
    public async Task Rejected_payment_decision_requires_transaction_scoped_reason()
    {
        var original = PaymentRequestBuilder.Parse(PaymentRequestBuilder.Build(new()
        {
            From="PAPSS", To="BANKSOSIXXX", EndToEndId="E2E-1", TxId="TX-1", Amount=10, Currency="SOS", Ustrd="test",
            LocalInstrument="INST", CategoryPurpose="CASH", Debtor=new() { Name="D", Account="D1", Address="A", AccountType="ACCT", AgentBIC="PAPSS", Issuer="I" },
            Creditor=new() { Name="C", Account="C1", Address="A", AccountType="ACCT", AgentBIC="BANKSOSIXXX", Issuer="I" }
        }).document)!;
        var decision = XDocument.Parse(PaymentRequestResponseBuilder.Build(new() { From="BANKSOSIXXX", To="PAPSS", Original=original, Status="RJCT", Reason="MS03" }));
        decision.Descendants().Where(x => x.Name.LocalName == "StsRsnInf").Remove();
        var header = decision.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").AddAfterSelf(new XElement(header.Name.Namespace + "BizSvc", Options().SecurityProfile));

        await Assert.ThrowsAsync<InvalidDataException>(() => Client(new()).SubmitPaymentDecisionAsync(Binding(), decision.ToString(SaveOptions.DisableFormatting), CancellationToken.None));
    }

    [Fact]
    public async Task Verification_request_uses_one_identifier_for_bah_msgid_and_verification_id()
    {
        var directory = new DirectoryScenario();
        var admission = await Client(directory).VerifyAsync(Binding(), new() { ToBIC="BANKKE00XXX", Alias="A1", Type="BBAN" }, CancellationToken.None);

        var request = XDocument.Parse(directory.FinancialRequest!);
        string Single(string name) => request.Descendants().Single(x => x.Name.LocalName == name).Value;
        Assert.Equal(admission.RequestMessageId, Single("BizMsgIdr"));
        Assert.Equal(admission.RequestMessageId, request.Descendants().Single(x => x.Name.LocalName == "Assgnmt").Elements().Single(x => x.Name.LocalName == "MsgId").Value);
        Assert.Equal(admission.RequestMessageId, request.Descendants().Single(x => x.Name.LocalName == "Vrfctn").Elements().Single(x => x.Name.LocalName == "Id").Value);
    }

    [Fact]
    public async Task All_operations_use_the_single_signed_wp_sips_ingress()
    {
        var directory = new DirectoryScenario(); var client = Client(directory); var binding = Binding();
        await client.VerifyAsync(binding, new() { ToBIC="BANKKE00XXX", Alias="A1", Type="BBAN" }, CancellationToken.None);
        await client.PayAsync(binding, Payment(), CancellationToken.None);
        await client.GetStatusAsync(binding, new StatusRequestDto { ToBIC="BANKKE00XXX", TxId="TX-1", EndToEnd="E2E-1" }, CancellationToken.None);
        await client.ReturnAsync(binding, new() { ToBIC="BANKKE00XXX", OriginalAmount=10, OriginalCurrency="SOS", OriginalTxId="TX-1", OriginalEndToEndId="E2E-1", ReturnId="RET-1", Reason="DUPL", AdditionalInfo="test", LocalInstrument="INST", CategoryPurpose="CASH" }, CancellationToken.None);
        await client.GetReadinessAsync(binding, new("PAPSS-BANK-1", null), CancellationToken.None);
        await client.DiscoverAsync(binding, new(null, null, "BANKKE00XXX", null), CancellationToken.None);
        await client.GetFxAsync(binding, new("SO", "KE", "SOS", "KES", "BANKKE00XXX", "INST", 10, false, null), CancellationToken.None);
        var positions=await client.GetPositionsAsync(binding,new(20),CancellationToken.None);
        Assert.Equal(100m,Assert.Single(positions.Positions).ClosingBalance!.Amount);
        Assert.Equal(10, directory.Requests.Count);
        Assert.All(directory.Requests, x => Assert.Equal("/sips/messages", x.Path));
    }

    private static PapssFacingSipsClient Client(DirectoryScenario directory, PapssFacingOptions? configured = null)
    {
        var options = configured ?? Options();
        var signer = new Mock<INativeSigner>();
        signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<string>())).Returns<string, XadesProfile, string>((xml, _, _) => xml);
        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(x => x.VerifyWithProvenance(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<CancellationToken>())).ReturnsAsync((string xml, XadesProfile _, CancellationToken _) =>
        {
            var header = XDocument.Parse(xml).Descendants().Single(x => x.Name.LocalName == "AppHdr");
            string Value(string name) => header.Elements().Single(x => x.Name.LocalName == name).Value;
            return new SignatureVerificationResult(true, new VerboseResult(), new("PAPSS", "CA", "test", "PAPSS", "issuer", "1", "hash", true, "v1", Value("MsgDefIdr"), Value("BizSvc"), "hash", DateTimeOffset.UtcNow));
        });
        return new(options, new HttpClient(directory), signer.Object, verifier.Object, new PapssHealthState(), NullLogger<PapssFacingSipsClient>.Instance);
    }

    private static PapssParticipantBinding Binding() => new("BANKSOSIXXX", "SO", ["SOS"], "bank-a", null);
    private static string Corridor(DirectoryScenario directory, string name) => XDocument.Parse(directory.FinancialRequest!).Descendants().Single(x => x.Name.LocalName == name).Value;
    private static PaymentRequestDto Payment() => new() { Rail="PAPSS", ToBIC="BANKKE00XXX", LocalInstrument="INST", CategoryPurpose="CASH", EndToEndId="E2E-1", TxId=Guid.NewGuid().ToString("N"), Amount=10, Currency="SOS", SenderCountry="SO", ReceiverCountry="KE", SenderCurrency="SOS", ReceiverCurrency="KES", DebtorName="Debtor", DebtorAccount="D1", DebtorAccountType="ACCT", DebtorAgentBIC="BANKSOSIXXX", CreditorName="Creditor", CreditorAccount="C1", CreditorAccountType="ACCT", CreditorAgentBIC="BANKKE00XXX", RemittanceInformation="test" };
    private static PapssFacingOptions Options() => new() { Enabled=true, IsoIngressUrl="https://papss.test/sips/messages", AllowedHosts=["papss.test"], RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001", RequestTimeoutSeconds=5, ReadinessStaleSeconds=300 };

    private sealed class DirectoryScenario : HttpMessageHandler
    {
        public int DiscoveryCount { get; set; } = 1;
        public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "ACTIVE";
        public bool Online { get; set; } = true;
        public string Country { get; set; } = "KE";
        public string[] Currencies { get; set; } = ["KES"];
        public string[] PaymentSchemas { get; set; } = ["INST"];
        public bool WrongAdmissionCorrelation { get; set; }
        public string AdmissionCode { get; set; } = "RECEIVED_AND_DURABLY_ADMITTED";
        public string AdmissionDescription { get; set; } = "Technical admission only.";
        public string? FinancialRequest { get; set; }
        public List<(string Path, string Profile)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var xml = await request.Content!.ReadAsStringAsync(cancellationToken);
            var parsed = XDocument.Parse(xml); var header = parsed.Descendants().Single(x => x.Name.LocalName == "AppHdr");
            string Header(string name) => header.Elements().Single(x => x.Name.LocalName == name).Value;
            var profile = Header("BizSvc"); Requests.Add((request.RequestUri!.AbsolutePath, profile));
            string response;
            if (Header("MsgDefIdr") == WpSipsMessageTypes.StaticDataRequest)
            {
                var requestMessageId = Header("BizMsgIdr"); var serviceRequestId = parsed.Descendants().Single(x => x.Name.LocalName == "MsgId").Value;
                var responseId = "RSP-" + Guid.NewGuid().ToString("N")[..20];
                var responseHeader = new BusinessHeader("PAPSS", "BANKSOSIXXX", responseId, WpSipsMessageTypes.StaticDataReport, profile, ObservedAt, requestMessageId);
                var participant = new Participant("PAPSS-BANK-1", "BANKKE00XXX", "Remote Bank", Country, Status, PaymentSchemas, Currencies, Online, false);
                response = profile switch
                {
                    WpSipsProfiles.Participant => WpSipsInformationMessageBuilder.BuildParticipantResponse(responseHeader, responseId, serviceRequestId, new(Enumerable.Repeat(participant, DiscoveryCount).ToArray())),
                    WpSipsProfiles.Readiness => WpSipsInformationMessageBuilder.BuildReadinessResponse(responseHeader, responseId, serviceRequestId, new(participant)),
                    WpSipsProfiles.Position => WpSipsInformationMessageBuilder.BuildPositionResponse(responseHeader,responseId,serviceRequestId,new([new(1,"RCON-1",ObservedAt,ObservedAt,false,0,new(10,"USD",1,0),new(5,"USD",1,null),new(15,"USD",2,0),new(85,"USD"),new(100,"USD"))])),
                    _ => WpSipsInformationMessageBuilder.BuildFxResponse(responseHeader, responseId, serviceRequestId, new([new(130, "MID", ObservedAt)], new(10, "SOS"), new(1300, "KES"), new(1300, "KES"), null, null))
                };
            }
            else
            {
                FinancialRequest = xml;
                if (WrongAdmissionCorrelation) parsed.Descendants().Single(x => x.Name.LocalName == "BizMsgIdr").Value = "ANOTHER-MESSAGE";
                response = AdminMessageBuilder.BuildForRejectedEnvelope(parsed.ToString(SaveOptions.DisableFormatting), "PAPSS-ADMISSION-1", DateTimeOffset.UtcNow, AdmissionCode, description: AdmissionDescription);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/xml") };
        }
    }
}
