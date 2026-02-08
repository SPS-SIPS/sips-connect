using SIPS.Core.Models;
namespace SIPS.Core.Interfaces;

public interface IAuthService
{
    Task<(LoginResponse? Token, string? Error)> LoginAsync(CancellationToken cancellationToken = default);
}