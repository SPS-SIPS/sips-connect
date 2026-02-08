namespace SIPS.PostgreSQL.Enums;

public enum ISOMessageType
{
    VerificationRequest = 1,
    TransactionRequest,
    StatusRequest,
    ReturnRequest,
    VerificationResponse,
    TransactionResponse,
    StatusResponse,
    ReturnResponse,
    InvalidMessageType
}