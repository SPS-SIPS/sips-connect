using System;
using static SIPS.Core.Constants;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.Responses;

public interface IResponseFactory
{
    PaymentRequestResponseBuilder.Response BuildPaymentInitial(PaymentRequestBuilder.Request request);
    PaymentStatusRequestResponseBuilder.Response BuildPaymentStatusInitial(PaymentStatusRequestBuilder.Request request);
}

public sealed class ResponseFactory : IResponseFactory
{
    public PaymentRequestResponseBuilder.Response BuildPaymentInitial(PaymentRequestBuilder.Request request)
    {
        return new PaymentRequestResponseBuilder.Response
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Original = request,
            Status = RJCT,
            Reason = MISS,
        };
    }

    public PaymentStatusRequestResponseBuilder.Response BuildPaymentStatusInitial(PaymentStatusRequestBuilder.Request request)
    {
        return new PaymentStatusRequestResponseBuilder.Response
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Status = RJCT,
            Reason = MISS,
            Original = new PaymentRequestBuilder.Request
            {
                From = request.From,
                To = request.To,
                MsgDefIdr = request.MsgDefIdr,
                BizMsgIdr = request.BizMsgIdr,
                MsgId = request.MsgId,
                CreDt = request.CreDt,
                TxId = request.OrgnlTxId,
                EndToEndId = request.OriginalEndToEnd,
            }
        };
    }
}
