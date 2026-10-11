using SIPS.ISO20022.Helpers;
using Xunit;

namespace SIPS.ISO20022.Tests;

public class PapssReasonCodesTests
{
    // TVR UAT 2026-10-11: the core bank correctly answered "AC01" (creditor account not found), but the mapper's
    // Confirmed set did not include it, so it was silently demoted to the generic MS03 fallback - the one value
    // PAPSS has rejected outright (x-papssrtp-reqsts: RJCT/1026), both as Rsn/Cd and Rsn/Prtry. A specific, correct
    // answer must never be downgraded to the fallback.
    [Theory]
    [InlineData("AC01")]
    [InlineData("AC04")]
    [InlineData("AC06")]
    [InlineData("ac01")]
    public void A_specific_account_rejection_code_is_never_downgraded_to_the_generic_fallback(string coreBankReason) =>
        Assert.Equal(coreBankReason.ToUpperInvariant(), PapssReasonCodes.FromCoreBankReason(coreBankReason));

    [Fact]
    public void An_unrecognized_reason_falls_back_to_the_generic_code() =>
        Assert.Equal(PapssReasonCodes.GenericError, PapssReasonCodes.FromCoreBankReason("NotFound"));

    [Fact]
    public void An_empty_reason_falls_back_to_the_generic_code() =>
        Assert.Equal(PapssReasonCodes.GenericError, PapssReasonCodes.FromCoreBankReason(null));

    [Theory]
    [InlineData("Insufficient funds")]
    [InlineData("AC02")]
    public void Insufficient_funds_maps_to_the_clearing_level_code(string coreBankReason) =>
        Assert.Equal(PapssReasonCodes.InsufficientFunds, PapssReasonCodes.FromCoreBankReason(coreBankReason));
}
