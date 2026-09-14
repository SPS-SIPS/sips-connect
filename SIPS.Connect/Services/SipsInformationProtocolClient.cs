using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.WpSips;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Connect.Services;

public sealed class SipsInformationProtocolClient(INativeSigner signer, INativeVerifier verifier)
{
    public string CreateFxRequest(BusinessHeader header,string requestId,FxRateRequest request)=>Sign(WpSipsInformationMessageBuilder.BuildFxRequest(header,requestId,request));
    public string CreateParticipantRequest(BusinessHeader header,string requestId,ParticipantDiscoveryRequest request)=>Sign(WpSipsInformationMessageBuilder.BuildParticipantRequest(header,requestId,request));
    public string CreateReadinessRequest(BusinessHeader header,string requestId,ReadinessRequest request)=>Sign(WpSipsInformationMessageBuilder.BuildReadinessRequest(header,requestId,request));
    public async ValueTask<WpSipsMessage<object>> VerifyResponseAsync(string signedRequest,string signedResponse,CancellationToken ct=default)
    {
        var verified=await verifier.VerifyWithProvenance(signedResponse,XadesProfile.WpSipsPapss,ct);if(!verified.Result||verified.Signer is null)throw new UnauthorizedAccessException("SIPS response signature/provenance validation failed.");
        WpSipsProtocolValidator.ValidateCorrelation(signedRequest,signedResponse);return WpSipsInformationMessageParser.Parse(signedResponse);
    }
    private string Sign(string xml){WpSipsProtocolValidator.Validate(xml);return signer.SignEnvelope(xml,XadesProfile.WpSipsPapss);}
}
