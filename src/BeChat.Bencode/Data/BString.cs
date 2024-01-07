using System.Text;

namespace BeChat.Bencode.Data;

public sealed class BString : BencodedBase<ReadOnlyMemory<byte>>
{
    private ReadOnlyMemory<byte> _bytes;

    public override ReadOnlyMemory<byte> Value => _bytes;
    public override BencodedType Type => BencodedType.String;
    
    public override ReadOnlyMemory<byte> AsBytes()
    {
        return _bytes.ToArray();
    }

    public override long AsInteger()
    {
        throw new InvalidCastException("Type cannot be casted to integer");
    }

    public override IList<BencodedBase> AsList()
    {
        throw new InvalidCastException("Type cannot be casted to list");
    }

    public override IDictionary<string, BencodedBase> AsDictionary()
    {
        throw new InvalidCastException("Type cannot be casted to dictionary");
    }

    public BString()
    {
        _bytes = Array.Empty<byte>();
    }

    public BString(string source)
    {
        _bytes = Encoding.UTF8.GetBytes(source);
    }

    public BString(ReadOnlyMemory<byte> span)
    {
        _bytes = span;
    }
    
    public BString(byte[] buffer)
    {
        _bytes = buffer;
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(_bytes.ToArray());
    }

    public override object Clone()
    {
        return new BString(_bytes);
    }
}