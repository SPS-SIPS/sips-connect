using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace SIPS.PostgreSQL.Security;
public class EncryptionConverters
{
    public static ValueConverter<string, string> GetEncryptionConverter()
    {
        return new ValueConverter<string, string>(
            v => EncryptionHelper.Encrypt(v),
            v => EncryptionHelper.Decrypt(v));
    }
}