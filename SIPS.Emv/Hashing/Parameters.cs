namespace SIPS.Emv.Hashing;
public sealed class Parameters(string name, int hashSize, ulong poly, ulong init, bool refIn, bool refOut, ulong xorOut, ulong check)
{

    public ulong Check { get; private set; } = check;
    public int HashSize { get; private set; } = hashSize;
    public ulong Init { get; private set; } = init;
    public string Name { get; private set; } = name;
    public ulong Poly { get; private set; } = poly;
    public bool RefIn { get; private set; } = refIn;
    public bool RefOut { get; private set; } = refOut;
    public ulong XorOut { get; private set; } = xorOut;
}