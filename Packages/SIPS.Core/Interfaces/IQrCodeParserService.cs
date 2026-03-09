using SIPS.Core.Models;

namespace SIPS.Core.Interfaces;

public interface IQrCodeParserService
{
    QrCodeData Parse(string qrCode);
}
