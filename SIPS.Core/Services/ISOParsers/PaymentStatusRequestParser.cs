using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentStatusRequestParser
{
    bool TryParse(string message, out PaymentStatusRequestBuilder.Request request);
}

public sealed class PaymentStatusRequestParser : IPaymentStatusRequestParser
{
    public bool TryParse(string message, out PaymentStatusRequestBuilder.Request request)
    {
        try
        {
            request = PaymentStatusRequestBuilder.Parse(message);
            if (request == null || request.OrgnlTxId == null || request.OriginalEndToEnd == null)
            {
                return false;
            }
            return true;
        }
        catch
        {
            request = null!;
            return false;
        }
    }
}
