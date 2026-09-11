using SIPS.ISO20022.Helpers;
using FluentAssertions;
using Xunit;
using System.IO;
using System;

namespace SIPS.Core.Tests.Tests;

public sealed class PapssCorridorTests
{
    [Fact]
    public void Payment_round_trip_preserves_papss_corridor()
    {
        var request = Request();
        request.PapssRectify(new("SO", "KE", "SOS", "KES"));
        var parsed = PaymentRequestBuilder.Parse(PaymentRequestBuilder.Build(request).document);
        parsed.PapssCorridor.Should().Be(request.PapssCorridor);
    }

    [Fact]
    public void Contradictory_sender_currency_is_rejected()
    {
        var request = Request();
        request.PapssRectify(new("SO", "KE", "USD", "KES"));
        var xml = PaymentRequestBuilder.Build(request).document;
        var act = () => PaymentRequestBuilder.Parse(xml);
        act.Should().Throw<InvalidOperationException>();
    }

    private static PaymentRequestBuilder.Request Request() => new()
    {
        From = "BANKSOSIXXX", To = "BANKKE00XXX", LocalInstrument = "INST", CategoryPurpose = "CASH",
        TxId = "TX-1", EndToEndId = "E2E-1", Amount = 10, Currency = "SOS",
        Ustrd = "test",
        Debtor = new() { Name = "Debtor", Account = "D1", AccountType = "ACCT", AgentBIC = "BANKSOSIXXX" },
        Creditor = new() { Name = "Creditor", Account = "C1", AccountType = "ACCT", AgentBIC = "BANKKE00XXX" }
    };
}

internal static class PapssRequestTestExtensions
{
    internal static void PapssRectify(this PaymentRequestBuilder.Request request, PaymentRequestBuilder.PapssCorridorData corridor) =>
        request.PapssCorridor = corridor;
}
