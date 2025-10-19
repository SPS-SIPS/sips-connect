using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IPayeeVerificationRequestParser
{
    bool TryParse(string xml, out PayeeVerificationBuilder.Request request);
}

public sealed class PayeeVerificationRequestParser : IPayeeVerificationRequestParser
{
    public bool TryParse(string xml, out PayeeVerificationBuilder.Request request)
    {
        request = PayeeVerificationBuilder.Parse(xml);
        return request != null;
    }
}
