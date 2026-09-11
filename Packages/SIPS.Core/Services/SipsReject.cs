using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services;

internal static class SipsReject
{
    internal static string Create(string rejectedEnvelope, string reasonCode, string description)
    {
        var responseId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        return AdminMessageBuilder.BuildForRejectedEnvelope(rejectedEnvelope, responseId, createdAt, reasonCode, description: description);
    }
}
