namespace SIPS.Core.Models;
public record LoginResponse(string AccessToken, Guid RefreshToken, long ExpiresIn, long RefreshExpiresIn);