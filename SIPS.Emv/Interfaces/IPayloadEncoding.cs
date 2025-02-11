namespace SIPS.Emv.Interfaces;
public interface IPayloadEncoding<T>
{
    string GeneratePayload(T payload);
}