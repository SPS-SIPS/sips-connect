namespace SIPS.Connect;

public static class Constants
{
    public const string DomainId = "so.somqr.sips";
    public const string VerificationRequest = "VerificationRequest";
    public const string VerificationResponse = "VerificationResponse";
    public const string PaymentRequest = "PaymentRequest";
    public const string PaymentResponse = "PaymentResponse";
    public const string StatusRequest = "StatusRequest";
    public const string ReturnRequest = "ReturnRequest";
    public const string ReturnResponse = "ReturnResponse";
}

public static class PointOfInitializationMethod
{
    public const string StaticQR = "11";
    public const string DynamicQR = "12";

    public static string GetQRType(decimal amount) => amount > 0 ? DynamicQR : StaticQR;
}