namespace SIPS.Example.Consumer.Enums;
public sealed class TransactionStatus : Enumeration<string>
{
    public static TransactionStatus Pending = new("PMD", "Pending");
    public static TransactionStatus Rejected = new("RJCT", "Rejected");
    public static TransactionStatus AcceptedSettlementCompleted = new("ACSC", "Accepted Settlement Completed");
    public static TransactionStatus Returned = new("RTN", "Returned");

    public TransactionStatus()
    {
    }
    public TransactionStatus(string id, string name)
        : base(id, name)
    {
    }
}
