namespace SIPS.Emv.Models;

public class Tlv(string tag, int length, string value)
{
    public int Tag { get; set; } = int.Parse(tag);

    public int Length { get; set; } = length;

    public string Value { get; set; } = value;
    public ICollection<Tlv> ChildNodes { get; set; } = [];
}
