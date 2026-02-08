using System.Reflection;
using System.Text;

namespace SIPS.Emv.Extensions;
public static class TlvExtensions
{
    public static string ToHex(this byte[] bytes, bool upperCase)
    {
        var sb = new StringBuilder(bytes.Length * 2);

        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
        }

        return sb.ToString();
    }

    public static string GetLast(this string input, int tailLength)
    {
        if (tailLength >= input.Length)
        {
            return input;
        }

        return input[^tailLength..];
    }

    public static void SetAndCastValue(this PropertyInfo property, object obj, string value)
    {
        if (property.PropertyType.IsAssignableFrom(typeof(int)))
        {
            property.SetValue(obj, int.Parse(value));
        }
        else if (property.PropertyType.IsAssignableFrom(typeof(decimal)))
        {
            property.SetValue(obj, decimal.Parse(value));
        }
        else
        {
            property.SetValue(obj, value);
        }
    }
}