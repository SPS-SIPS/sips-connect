using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class ParticipantCallbackJsonAdapterTests
{
    [Fact]
    public void Callback_scope_selects_participant_profile_and_restores_default()
    {
        var options = new JsonAdapterOptions { Endpoints = new()
        {
            ["CB_PaymentRequest"] = Mapping("defaultField"),
            ["bank-a.CB_PaymentRequest"] = Mapping("participantField")
        }};
        var context = new ParticipantCallbackContext();
        var adapter = new ParticipantCallbackJsonAdapter(new JsonAdapter(options, NullLogger<JsonAdapter>.Instance), options, context);
        var input = new JsonObject { ["value"] = "ok" };

        Assert.True(adapter.Transform(input, "CB_PaymentRequest").ContainsKey("defaultField"));
        using (context.Push(new("bank-a", "BANKSOSIXXX", "bank-a", "https://bank.test/callback")))
            Assert.True(adapter.Transform(input, "CB_PaymentRequest").ContainsKey("participantField"));
        Assert.True(adapter.Transform(input, "CB_PaymentRequest").ContainsKey("defaultField"));
    }

    private static EndpointMapping Mapping(string field) => new() { FieldMappings = [new() { InternalField = field, UserField = "value", Type = "string" }] };
}
