using System;
using FluentAssertions;
using SIPS.Core.Services.Responses;
using SIPS.ISO20022.Helpers;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class ResponseFactoryTests
{
    [Fact]
    public void BuildPaymentInitial_MapsFields_AndDefaults()
    {
        var req = new PaymentRequestBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "CT",
            BizMsgIdr = "BIZ",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            TxId = "TX",
            EndToEndId = "E2E"
        };

        var f = new ResponseFactory();
        var rs = f.BuildPaymentInitial(req);

        rs.From.Should().Be(req.To);
        rs.To.Should().Be(req.From);
        rs.MsgDefIdr.Should().Be(req.MsgDefIdr);
        rs.BizMsgIdr.Should().Be(req.BizMsgIdr);
        rs.MsgId.Should().Be(req.MsgId);
        rs.CreDt.Should().Be(req.CreDt);
        rs.Original.Should().BeSameAs(req);
        rs.Status.Should().Be(SIPS.Core.Constants.RJCT);
        rs.Reason.Should().Be(SIPS.Core.Constants.MISS);
    }

    [Fact]
    public void BuildPaymentStatusInitial_MapsFields_AndDefaults()
    {
        var req = new PaymentStatusRequestBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "CTST",
            BizMsgIdr = "BIZ",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            OrgnlTxId = "TX",
            OriginalEndToEnd = "E2E"
        };

        var f = new ResponseFactory();
        var rs = f.BuildPaymentStatusInitial(req);

        rs.From.Should().Be(req.To);
        rs.To.Should().Be(req.From);
        rs.MsgDefIdr.Should().Be(req.MsgDefIdr);
        rs.BizMsgIdr.Should().Be(req.BizMsgIdr);
        rs.MsgId.Should().Be(req.MsgId);
        rs.CreDt.Should().Be(req.CreDt);
        rs.Status.Should().Be(SIPS.Core.Constants.RJCT);
        rs.Reason.Should().Be(SIPS.Core.Constants.MISS);
        rs.Original.Should().NotBeNull();
        rs.Original.From.Should().Be(req.From);
        rs.Original.To.Should().Be(req.To);
        rs.Original.TxId.Should().Be(req.OrgnlTxId);
        rs.Original.EndToEndId.Should().Be(req.OriginalEndToEnd);
    }
}
