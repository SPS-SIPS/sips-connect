namespace SIPS.ISO20022.Interfaces;
interface IMessage
{
    string From { get; set; }
    string To { get; set; }
    string MsgDefIdr { get; set; }
    string BizMsgIdr { get; set; }
    DateTime CreDt { get; set; }
    string MsgId { get; set; }
}