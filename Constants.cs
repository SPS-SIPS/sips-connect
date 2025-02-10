namespace SIPS.Connect;
public static class Constants
{
    public const string DomainId = "so.somqr.SIPS";
    public const string PayloadFormatIndicator = "02";
}

public static class PointOfInitializationMethod
{
    public const string StaticQR = "11";
    public const string DynamicQR = "12";

    public static string GetQRType(decimal amount) => amount > 0 ? DynamicQR : StaticQR;
}