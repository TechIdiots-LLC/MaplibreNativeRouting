using System.Text;

namespace MaplibreNative.Routing.Core.Mvt;

internal ref struct ProtobufReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public ProtobufReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public bool HasMore => _position < _buffer.Length;

    public int ReadFieldTag(out int wireType)
    {
        var varint = ReadVarint32();
        wireType = (int)(varint & 0x7);
        return (int)(varint >> 3);
    }

    public uint ReadVarint32()
    {
        uint result = 0;
        int shift = 0;
        while (true)
        {
            byte b = _buffer[_position++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift >= 35) throw new InvalidDataException("Varint too long");
        }
    }

    public ulong ReadVarint64()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = _buffer[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift >= 70) throw new InvalidDataException("Varint too long");
        }
    }

    public int ReadSInt32()
    {
        var n = ReadVarint32();
        return (int)((n >> 1) ^ (~(n & 1) + 1));
    }

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        int len = (int)ReadVarint32();
        var span = _buffer.Slice(_position, len);
        _position += len;
        return span;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadLengthDelimited());

    public float ReadFloat()
    {
        var val = BitConverter.ToSingle(_buffer.Slice(_position, 4));
        _position += 4;
        return val;
    }

    public double ReadDouble()
    {
        var val = BitConverter.ToDouble(_buffer.Slice(_position, 8));
        _position += 8;
        return val;
    }

    public int ReadFixed32()
    {
        var val = BitConverter.ToInt32(_buffer.Slice(_position, 4));
        _position += 4;
        return val;
    }

    public long ReadFixed64()
    {
        var val = BitConverter.ToInt64(_buffer.Slice(_position, 8));
        _position += 8;
        return val;
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint64(); break;
            case 1: _position += 8; break;
            case 2: _position += (int)ReadVarint32(); break;
            case 5: _position += 4; break;
            default: throw new InvalidDataException($"Unknown wire type {wireType}");
        }
    }
}
