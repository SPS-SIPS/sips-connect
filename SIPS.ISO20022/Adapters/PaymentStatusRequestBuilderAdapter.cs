using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;

namespace SIPS.ISO20022.Adapters;

public sealed class PaymentStatusRequestBuilderAdapter : IPaymentStatusRequestBuilder
{
    public PaymentStatusRequestBuilder.Request Parse(string content)
        => PaymentStatusRequestBuilder.Parse(content);
}
