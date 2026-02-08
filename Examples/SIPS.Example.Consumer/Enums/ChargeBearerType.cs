namespace SIPS.Example.Consumer.Enums;
public sealed class ChargeBearerType : Enumeration<string>
{
    public static ChargeBearerType BorneByDebtor = new("DEBT", "Borne By Debtor");
    public static ChargeBearerType BorneByCreditor = new("CRED", "Borne By Creditor");
    public static ChargeBearerType Shared = new("SHAR", "Shared");
    public static ChargeBearerType FollowingServiceLevel = new("SLEV", "Following Service Level");
    public ChargeBearerType()
    {
    }
    public ChargeBearerType(string id, string name)
        : base(id, name)
    {
    }
}
