using System.Xml.Linq;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentStatusReportParser
{
    bool TryParse(string xml, out PaymentRequestResponseBuilder.Response? request);
}

public sealed class PaymentStatusReportParser : IPaymentStatusReportParser
{
    public bool TryParse(string xml, out PaymentRequestResponseBuilder.Response? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var parsed = PaymentRequestResponseBuilder.Parse(xml);
            if (parsed == null) return false;

            request = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

}
