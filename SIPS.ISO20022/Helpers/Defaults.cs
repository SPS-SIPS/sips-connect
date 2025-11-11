namespace SIPS.ISO20022.Helpers;

public static class Defaults
{
    public static void EnsurePersonDefaults(Person? p)
    {
        if (p == null) return;
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "NA";
        if (string.IsNullOrWhiteSpace(p.Address)) p.Address = "NA";
        if (string.IsNullOrWhiteSpace(p.Account)) p.Account = "NA";
        if (string.IsNullOrWhiteSpace(p.AccountType)) p.AccountType = "ACCT";
        if (string.IsNullOrWhiteSpace(p.Issuer)) p.Issuer = "C";
    }

    public static void EnsureOriginalDefaults(PaymentRequestBuilder.Request? original)
    {
        if (original == null) return;
        EnsurePersonDefaults(original.Debtor);
        EnsurePersonDefaults(original.Creditor);
        if (string.IsNullOrWhiteSpace(original.EndToEndId))
            original.EndToEndId = string.IsNullOrWhiteSpace(original.TxId) ? "E2" : original.TxId;
        if (string.IsNullOrWhiteSpace(original.Currency))
            original.Currency = "USD";
        // BizMsgIdr/MsgDefIdr/CreDt handled by caller if needed
    }

    public static void EnsureReturnDefaults(ReturnPaymentRequestBuilder.Request? original)
    {
        if (original == null) return;
        if (string.IsNullOrWhiteSpace(original.From)) original.From = "NA";
        if (string.IsNullOrWhiteSpace(original.To)) original.To = "NA";
        if (original.CreDt == default) original.CreDt = DateTime.UtcNow;
        if (original.NumberOfTransactions <= 0) original.NumberOfTransactions = 1;
        if (string.IsNullOrWhiteSpace(original.OriginalEndToEnd))
            original.OriginalEndToEnd = string.IsNullOrWhiteSpace(original.OrgnlTxId) ? "E2E" : original.OrgnlTxId;
        if (string.IsNullOrWhiteSpace(original.OriginalCurrency)) original.OriginalCurrency = "USD";
        if (string.IsNullOrWhiteSpace(original.ReturnReason)) original.ReturnReason = "NA";
        if (string.IsNullOrWhiteSpace(original.AdditionalInfo)) original.AdditionalInfo = string.Empty;
    }

    public static void EnsureVerificationDefaults(PayeeVerificationBuilder.Request? req)
    {
        if (req == null) return;
        if (string.IsNullOrWhiteSpace(req.From)) req.From = "NA";
        if (string.IsNullOrWhiteSpace(req.To)) req.To = "NA";
        if (req.CreDt == default) req.CreDt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(req.Alias)) req.Alias = "NA";
        if (string.IsNullOrWhiteSpace(req.Type)) req.Type = "NA";
        // SIPSRequestId may be null for some tests; leave as-is
    }
}
