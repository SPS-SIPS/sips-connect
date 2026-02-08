namespace SIPS.XMLDsig.Xades.Interfaces;
public interface INativeSigner
{
    string SignEnvelope(string message, string algorithm = "SHA1withRSA");
}
