namespace SIPS.Core;
public static class Constants
{
    public const string CB_ReturnRequest = "CB_ReturnRequest";
    public const string CB_ReturnResponse = "CB_ReturnResponse";
    public const string CB_StatusRequest = "CB_StatusRequest";
    public const string CB_StatusResponse = "CB_StatusResponse";
    public const string CB_PaymentStatusResponse = "CB_PaymentStatusResponse";
    public const string CB_PaymentRequest = "CB_PaymentRequest";
    public const string CB_VerificationRequest = "CB_VerificationRequest";
    public const string CB_VerificationResponse = "CB_VerificationResponse";
    /// <summary>Asynchronous payee-verification result (acmt.024) delivered to the bank, e.g. for PAPSS name enquiries.</summary>
    public const string CB_VerificationResult = "CB_VerificationResult";
    public const string CB_CompletionNotification = "CB_CompletionNotification";
    public const string CB_CompletionNotificationResponse = "CB_CompletionNotificationResponse";
    public const string API_Key = "ApiKey";
    public const string API_Secret = "ApiSecret";

    public const string ACSC = "ACSC";
    public const string ACSP = "ACSP";
    public const string RJCT = "RJCT";
    public const string PDNG = "PDNG"; // Pending - awaiting final status
    public const string MISS = "MISS";
    public const string SUCC = "SUCC";
    public const string IBAN = "IBAN";
}