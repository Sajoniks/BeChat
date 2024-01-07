namespace BeChat.Bencode.Data;

public sealed class BInteger : BencodedBase<long>
{
    private long _number;
    
    public override BencodedType Type => BencodedType.Integer;
    
    public override ReadOnlyMemory<byte> AsBytes()
    {
        throw new InvalidCastException("Type cannot be casted to bytes");
    }

    public override long AsInteger()
    {
        return _number;
    }

    public override IList<BencodedBase> AsList()
    {
        throw new InvalidCastException("Type cannot be casted to list");
    }

    public override IDictionary<string, BencodedBase> AsDictionary()
    {
        throw new InvalidCastException("Type cannot be casted to dictionary");
    }

    public override string ToString()
    {
        return "";
    }

    public override object Clone()
    {
        return new BInteger(_number);
    }

    public override long Value => _number;

    public BInteger()
    {
        _number = 0;
    }
    
    public BInteger(long number)
    {
        _number = number;
    }
}