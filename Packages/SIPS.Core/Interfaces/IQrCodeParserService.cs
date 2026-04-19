using SIPS.ISO20022.Models;

namespace SIPS.Core.Interfaces;

public interface IQrCodeParserService
{
    QrCodeData Parse(string qrCode);
}
