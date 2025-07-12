namespace SIPS.Example.Consumer.Enums;

public sealed class CategoryPurpose : Enumeration<string>
{
    public static CategoryPurpose C2CCreditTransfer = new("C2CCRT", "C2C credit transfer");
    public static CategoryPurpose C2CRequestToPay = new("C2CRTP", "C2C Request To Pay");
    public static CategoryPurpose C2BBillPayment = new("C2BBPT", "C2B Bill Payment");
    public static CategoryPurpose C2BStaticQRPayment = new("C2BSQR", "C2B Static QR Payment");
    public static CategoryPurpose C2BDynamicQRPayment = new("C2BDQR", "C2B Dynamic QR Payment");
    public static CategoryPurpose C2BRequestToPay = new("C2BRTP", "C2B Request To Pay");
    public static CategoryPurpose C2GBillPayment = new("C2GBPT", "C2G Bill Payment");
    public static CategoryPurpose G2CBillPayment = new("G2CBPT", "G2C Bill Payment");
    public CategoryPurpose()
    {
    }
    public CategoryPurpose(string id, string name)
        : base(id, name)
    {
    }
}