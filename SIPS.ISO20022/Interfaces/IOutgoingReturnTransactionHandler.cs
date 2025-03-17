using SIPS.ISO20022.Models.DTOs;

namespace SIPS.ISO20022.Interfaces;

public interface IOutgoingReturnTransactionHandler
{
    Task<Response<ReturnPaymentResponseDto>> HandleAsync(ReturnPaymentRequestDto message, CancellationToken ct);
}