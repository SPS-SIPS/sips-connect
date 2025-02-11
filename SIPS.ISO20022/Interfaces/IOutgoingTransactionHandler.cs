using SIPS.ISO20022.Models.DTOs;

namespace SIPS.ISO20022.Interfaces;

public interface IOutgoingTransactionHandler
{
    Task<Response<PaymentResponseDto>> HandleAsync(PaymentRequestDto message, CancellationToken ct);
}