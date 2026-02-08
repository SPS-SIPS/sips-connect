namespace SIPS.Example.Consumer.Enums;

public sealed class SettlementMethods : Enumeration<string>
{
    public static SettlementMethods InstructedAgent = new("INDA", "Instructed Agent");
    public static SettlementMethods InstructingAgent = new("INGA", "Instructing Agent");
    public static SettlementMethods ClearingSystem = new("CLRG", "Clearing System");
    public SettlementMethods()
    {
    }
    public SettlementMethods(string id, string name)
        : base(id, name)
    {
    }
}

