public class EmvOptions
{
    public string AcquirerId { get; set; }  = string.Empty;
    public string FIType { get; set; } = string.Empty;
    public string FIName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public EmvTags? Tags { get; set; }
    public class EmvTags {
        public int MerchantIdentifier { get; set; }
        public int AcquirerTag { get; set; }
        public int MerchantIdTag { get; set; }
    }
}