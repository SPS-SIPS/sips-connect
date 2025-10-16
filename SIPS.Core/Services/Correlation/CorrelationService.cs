using System;

namespace SIPS.Core.Services.Correlation;

public interface ICorrelationService
{
    string Create(params string?[] parts);
}

public sealed class CorrelationService : ICorrelationService
{
    public string Create(params string?[] parts)
    {
        foreach (var p in parts)
        {
            if (!string.IsNullOrWhiteSpace(p))
                return p!;
        }
        return Guid.NewGuid().ToString("N");
    }
}
