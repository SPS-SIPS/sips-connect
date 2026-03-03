namespace SIPS.Core.Options;
public class CoreOptions
{
    public string BaseUrl { get; set; } = "";
    public string PublicKeysRepUrl { get; set; } = "";
    public string LoginEndpoint { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string BIC { get; set; } = "";
    public string SAFExpression { get; set; } = "";
    public int SAFPage { get; set; } = 40;
    public string SAFTimeZoneInfo { get; set; } = "";
    public int SAFMaxRetries { get; set; } = 10;
    public bool IncludeIdempotencyHeaders { get; set; } = false;
    public int HttpTimeoutSeconds { get; set; } = 15;
    public int CoreBankTimeoutSeconds { get; set; } = 3;
    public int DbPersistTimeoutSeconds { get; set; } = 10;
    public bool IncludeCoreBankOnListing { get; set; } = false;
    public int TransactionTimeoutMinutes { get; set; } = 60;
    public string TimeoutWorkerSchedule { get; set; } = "*/15 * * * *";

    // [CHANGE GUARD]: BPC SmartVista IPS 2024 SLA Constants. 
    // DO NOT modify these budgets without a formal IPS contract review.
    public int CallbackSlaSeconds { get; set; } = 10;
    public int CallbackInternalBudgetSeconds { get; set; } = 9;
    public bool VerificationOnlyMode { get; set; } = false;
}