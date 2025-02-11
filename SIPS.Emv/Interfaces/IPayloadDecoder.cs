namespace SIPS.Emv.Interfaces;

public interface IPayloadDecoder<T>
{
    T BuildPayload(ICollection<Tlv> collection);

    ICollection<Tlv> DecodeQR(string code, bool containsChildren, bool isP2P = false);

    string ValidateCrc(string code);
}