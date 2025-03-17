using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.XMLDsig.Xades.Services;

namespace SIPS.Core.Services;
public class Incoming(IIncomingVerificationHandler vr, IIncomingTransactionHandler ith, IIncomingTransactionStatusHandler psh, IIncomingReturnTransactionHandler rh) : IIncoming
{
    private readonly IIncomingVerificationHandler _vr = vr;
    private readonly IIncomingTransactionHandler _ith = ith;
    private readonly IIncomingTransactionStatusHandler _psh = psh;
    private readonly IIncomingReturnTransactionHandler _rh = rh;
    public async ValueTask<string> Handle(string isoMessage, CancellationToken ct)
    {
        var messageType = Transformers.GetMessageType(isoMessage);
        return messageType switch
        {
            "acmt.023.001.03" => await _vr.HandleAsync(isoMessage, CancellationToken.None),
            "pacs.008.001.10" => await _ith.HandleAsync(isoMessage, CancellationToken.None),
            "pacs.028.001.05" => await _psh.HandleAsync(isoMessage, CancellationToken.None),
            "pacs.004.001.11" => await _rh.HandleAsync(isoMessage, CancellationToken.None),
            _ => AdminMessage.Generate("Unsupported message type."),
        };
    }
}
