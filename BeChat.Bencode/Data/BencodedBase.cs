namespace BeChat.Bencode.Data;

public enum BencodedType
{
    Integer,
    String,
    List,
    Dictionary
}

public abstract class BencodedBase : ICloneable
{
    public abstract BencodedType Type { get; }

    public abstract ReadOnlyMemory<byte> AsBytes();
    public abstract long AsInteger();
    public abstract IList<BencodedBase> AsList();
    public abstract IDictionary<string, BencodedBase> AsDictionary();

    public abstract override String ToString();
    public abstract object Clone();
}

public abstract class BencodedBase<T> : BencodedBase
{
    public abstract T Value { get; }
}