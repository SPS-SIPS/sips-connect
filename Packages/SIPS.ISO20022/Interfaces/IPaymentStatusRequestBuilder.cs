using SIPS.ISO20022.Helpers;

namespace SIPS.ISO20022.Interfaces;

public interface IPaymentStatusRequestBuilder
{
    PaymentStatusRequestBuilder.Request Parse(string content);
}
