using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.WpSips;
using System;

namespace SIPS.Core.Tests;

internal static class WpSipsTestEnvelope
{
    internal static string Valid(string id="TEST-MSG") => WpSipsInformationMessageBuilder.BuildFxRequest(
        new("SENDER","RECEIVER",id,"admi.009.001.02",WpSipsProfiles.Fx,DateTimeOffset.UtcNow),"REQ-"+id,
        new("US","KE","USD","KES","BANK1","INST",10,false,null));
}
