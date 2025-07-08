using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.XMLDsig.Xades.Services;

namespace SIPS.Core.Services;

public class Incoming : IIncoming
{
    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> _handlers;

    public Incoming(
        IIncomingVerificationHandler vr,
        IIncomingTransactionHandler ith,
        IIncomingTransactionStatusHandler psh,
        IIncomingReturnTransactionHandler rh)
    {
        _handlers = new()
        {
            ["acmt.023.001.03"] = vr.HandleAsync,
            ["pacs.008.001.10"] = ith.HandleAsync,
            ["pacs.028.001.05"] = psh.HandleAsync,
            ["pacs.004.001.11"] = rh.HandleAsync
        };
    }

    public async ValueTask<string> Handle(string isoMessage, CancellationToken ct)
    {
        var messageType = Transformers.GetMessageType(isoMessage);
        if (_handlers.TryGetValue(messageType, out var handler))
        {
            return await handler(isoMessage, ct);
        }
        return AdminMessage.Generate("Unsupported message type.");
    }
}
