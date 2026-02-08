using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentRequestParser
{
    bool TryParse(string message, out PaymentRequestBuilder.Request request);
}

public sealed class PaymentRequestParser : IPaymentRequestParser
{
    public bool TryParse(string message, out PaymentRequestBuilder.Request request)
    {
        try
        {
            request = PaymentRequestBuilder.Parse(message);
            return request != null;
        }
        catch
        {
            request = null!;
            return false;
        }
    }
}
