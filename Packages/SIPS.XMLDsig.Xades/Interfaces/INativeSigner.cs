namespace SIPS.XMLDsig.Xades.Interfaces;
public interface INativeSigner
{
    string SignEnvelope(string message, string algorithm = "SHA256withRSA");
    string SignEnvelope(string message, XadesProfile profile, string algorithm = "SHA256withRSA")
        => profile == XadesProfile.IpsVendorLegacy
            ? SignEnvelope(message, algorithm)
            : throw new NotSupportedException("This signer does not implement the WP-SIPS/PAPSS XAdES profile.");
}
