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
}
