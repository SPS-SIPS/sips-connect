using SIPS.ISO20022.Enums;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.WpSips;
using Xunit;

namespace SIPS.ISO20022.Tests;

public class WpSipsProtocolTests
{
    static readonly DateTimeOffset Now = new(2026,9,5,8,0,0,TimeSpan.Zero);
    static BusinessHeader Request(string profile)=>new("SPS-A","SPS-B","MSG-REQ-1",WpSipsMessageTypes.StaticDataRequest,profile,Now);
    static BusinessHeader Response(string profile)=>new("SPS-B","SPS-A","MSG-RSP-1",WpSipsMessageTypes.StaticDataReport,profile,Now,"MSG-REQ-1");

    [Fact] public void Six_profiles_are_schema_valid_and_round_trip()
    {
        var participant=new Participant("BANK1","AAAAUS00","Bank","US","ACTIVE",["INSTANT"],["USD"],true,false);
        var messages=new[] {
            WpSipsInformationMessageBuilder.BuildFxRequest(Request(WpSipsProfiles.Fx),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null)),
            WpSipsInformationMessageBuilder.BuildFxResponse(Response(WpSipsProfiles.Fx),"MSG-RSP-1","REQ-1",new([new(130,"MID",Now)],new(10,"USD"),new(1300,"KES"),new(1300,"KES"),null,null)),
            WpSipsInformationMessageBuilder.BuildParticipantRequest(Request(WpSipsProfiles.Participant),"REQ-1",new(null,null,null,"BANK1")),
            WpSipsInformationMessageBuilder.BuildParticipantResponse(Response(WpSipsProfiles.Participant),"MSG-RSP-1","REQ-1",new([participant])),
            WpSipsInformationMessageBuilder.BuildReadinessRequest(Request(WpSipsProfiles.Readiness),"REQ-1",new("BANK1",null)),
            WpSipsInformationMessageBuilder.BuildReadinessResponse(Response(WpSipsProfiles.Readiness),"MSG-RSP-1","REQ-1",new(participant)) };
        foreach(var xml in messages){ try { WpSipsProtocolValidator.Validate(xml); } catch(WpSipsValidationException e) { throw new Xunit.Sdk.XunitException(string.Join(" | ",e.Diagnostics.Select(d=>$"{d.Stage}:{d.Code}:{d.Detail}"))); } Assert.NotNull(WpSipsInformationMessageParser.Parse(xml).Payload); }
    }

    [Fact] public void Schema_negative_and_profile_mismatch_fail()
    {
        var xml=WpSipsInformationMessageBuilder.BuildReadinessRequest(Request(WpSipsProfiles.Readiness),"REQ-1",new("BANK1",null));
        Assert.Throws<WpSipsValidationException>(()=>WpSipsProtocolValidator.Validate(xml.Replace(WpSipsProfiles.Readiness,WpSipsProfiles.Fx,StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(()=>WpSipsInformationMessageBuilder.BuildFxRequest(Request(WpSipsProfiles.Fx),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,"USD")));
    }
    [Fact] public void Bilateral_correlation_is_exact()
    {
        var request=WpSipsInformationMessageBuilder.BuildFxRequest(Request(WpSipsProfiles.Fx),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null));
        var response=WpSipsInformationMessageBuilder.BuildFxResponse(Response(WpSipsProfiles.Fx),"MSG-RSP-1","REQ-1",new([new(130,"MID",Now)],new(10,"USD"),new(1300,"KES"),new(1300,"KES"),null,null));
        WpSipsProtocolValidator.ValidateCorrelation(request,response);
        Assert.Throws<WpSipsValidationException>(()=>WpSipsProtocolValidator.ValidateCorrelation(request,response.Replace("REQ-1","OTHER",StringComparison.Ordinal)));
    }
    [Fact] public void Shared_admi002_is_schema_backed_and_correlated()
    {
        var request=WpSipsInformationMessageBuilder.BuildFxRequest(Request(WpSipsProfiles.Fx),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null));
        var reject=AdminMessageBuilder.BuildForRejectedEnvelope(request,"REJECT-1",Now,AdminRejectReasonCodes.DuplicateMessageConflict,description:"conflict");
        WpSipsProtocolValidator.Validate(reject);var parsed=System.Xml.Linq.XDocument.Parse(reject);Assert.Equal("MSG-REQ-1",parsed.Descendants().Single(x=>x.Name.LocalName=="Ref").Value);Assert.Contains("DUPLICATE_CONFLICT",reject);
    }
}
