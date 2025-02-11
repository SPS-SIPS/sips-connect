using System.Security.Cryptography;
using SIPS.Emv.Helpers;

namespace SIPS.Emv.Hashing;

public class Crc : HashAlgorithm
{
    private readonly ulong _mask;

    private readonly ulong[] _table = new ulong[256];

    private ulong _currentValue;

    public Crc(Parameters parameters)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        _mask = ulong.MaxValue >> (64 - HashSize);

        Init();
    }

    public override bool CanTransformMultipleBlocks
    {
        get
        {
            return base.CanTransformMultipleBlocks;
        }
    }

    public override int HashSize { get { return Parameters.HashSize; } }

    public Parameters Parameters { get; private set; }

    public ulong[] GetTable()
    {
        var res = new ulong[_table.Length];
        Array.Copy(_table, res, _table.Length);
        return res;
    }

    public override void Initialize()
    {
        _currentValue = Parameters.RefOut ? CrcHelper.ReverseBits(Parameters.Init, HashSize) : Parameters.Init;
    }

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        _currentValue = ComputeCrc(_currentValue, array, ibStart, cbSize);
    }

    protected override byte[] HashFinal()
    {
        return CrcHelper.ToBigEndianBytes(_currentValue ^ Parameters.XorOut);
    }

    private void Init()
    {
        CreateTable();
        Initialize();
    }

    private ulong ComputeCrc(ulong init, byte[] data, int offset, int length)
    {
        ulong crc = init;

        if (Parameters.RefOut)
        {
            for (int i = offset; i < offset + length; i++)
            {
                crc = (_table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8));
                crc &= _mask;
            }
        }
        else
        {
            int toRight = (HashSize - 8);
            toRight = toRight < 0 ? 0 : toRight;
            for (int i = offset; i < offset + length; i++)
            {
                crc = (_table[((crc >> toRight) ^ data[i]) & 0xFF] ^ (crc << 8));
                crc &= _mask;
            }
        }

        return crc;
    }

    private void CreateTable()
    {
        for (int i = 0; i < _table.Length; i++)
            _table[i] = CreateTableEntry(i);
    }

    private ulong CreateTableEntry(int index)
    {
        ulong r = (ulong)index;

        if (Parameters.RefIn)
            r = CrcHelper.ReverseBits(r, HashSize);
        else if (HashSize > 8)
            r <<= (HashSize - 8);

        ulong lastBit = (1ul << (HashSize - 1));

        for (int i = 0; i < 8; i++)
        {
            if ((r & lastBit) != 0)
                r = ((r << 1) ^ Parameters.Poly);
            else
                r <<= 1;
        }

        if (Parameters.RefIn)
            r = CrcHelper.ReverseBits(r, HashSize);

        return r & _mask;
    }
}