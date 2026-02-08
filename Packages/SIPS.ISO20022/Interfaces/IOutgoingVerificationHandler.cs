using SIPS.ISO20022.Models.DTOs;

namespace SIPS.ISO20022.Interfaces;

public interface IOutgoingVerificationHandler
{
    Task<Response<VerificationResponseDto>> HandleAsync(VerificationRequestDto message, CancellationToken ct);
}