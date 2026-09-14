using System.Text.Json;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class JsonAdapterPapssProfileTests
{
    private static readonly string[] Profiles =
    [
        "jsonAdapter.json",
        "jsonAdapter.node-a.json",
        "jsonAdapter.node-b.json"
    ];

    private static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["VerificationRequest"] = ["Rail", "Alias", "Type", "ToBIC"],
        ["PaymentRequest"] = ["Rail", "SenderCountry", "ReceiverCountry", "SenderCurrency", "ReceiverCurrency", "ToBIC", "LocalInstrument", "EndToEndId"],
        ["StatusRequest"] = ["Rail", "EndToEnd", "TxId", "ToBIC"],
        ["ReturnRequest"] = ["Rail", "ToBIC", "OriginalAmount", "OriginalCurrency", "OriginalTxId", "OriginalEndToEndId", "ReturnId"],
        ["PapssAdmissionResponse"] = ["RequestMessageId", "Code", "DurablyAdmitted"],
        ["ReadinessRequest"] = ["Rail", "PapssId", "Bic"],
        ["ReadinessResponse"] = ["Observation", "Error"],
        ["ParticipantDiscoveryRequest"] = ["Rail", "Online", "Type", "Bic", "PapssId"],
        ["ParticipantDiscoveryResponse"] = ["Participants", "Error"],
        ["FxRequest"] = ["Rail", "SenderCountry", "ReceiverCountry", "SenderCurrency", "ReceiverCurrency", "ReceiverBank", "LocalInstrument", "Amount", "IsInvoice", "InvoiceCurrency"],
        ["FxResponse"] = ["Rates", "SenderAmount", "ReceiverAmount", "InvoiceAmount", "Error"]
    };

    [Theory]
    [MemberData(nameof(ProfileNames))]
    public void Profile_contains_complete_Papss_contract(string profile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), profile)));
        var endpoints = document.RootElement.GetProperty("Endpoints");

        foreach (var (mapping, requiredFields) in RequiredFields)
        {
            Assert.True(endpoints.TryGetProperty(mapping, out var endpoint), $"{profile} is missing {mapping}");
            var actual = endpoint.GetProperty("FieldMappings")
                .EnumerateArray()
                .Select(field => field.GetProperty("InternalField").GetString())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var required in requiredFields)
                Assert.Contains(required, actual);
        }
    }

    public static TheoryData<string> ProfileNames()
    {
        var data = new TheoryData<string>();
        foreach (var profile in Profiles) data.Add(profile);
        return data;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "jsonAdapter.json")))
            current = current.Parent;

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate the SIPS repository root.");
    }
}
