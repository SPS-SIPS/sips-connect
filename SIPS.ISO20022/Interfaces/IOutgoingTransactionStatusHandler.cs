using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;

namespace SIPS.ISO20022.Interfaces;

public interface IOutgoingTransactionStatusHandler
{
    Task<Response<PaymentResponseDto>> HandleAsync(StatusRequestDto message, CancellationToken ct);
}