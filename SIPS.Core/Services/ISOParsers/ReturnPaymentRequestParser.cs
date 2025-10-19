using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IReturnPaymentRequestParser
{
    bool TryParse(string xml, out ReturnPaymentRequestBuilder.Request request);
}

public sealed class ReturnPaymentRequestParser : IReturnPaymentRequestParser
{
    public bool TryParse(string xml, out ReturnPaymentRequestBuilder.Request request)
    {
        request = ReturnPaymentRequestBuilder.Parse(xml);
        if (request == null || request.OrgnlTxId == null || request.OriginalEndToEnd == null || request.ReturnId == null)
            return false;
        return true;
    }
}
