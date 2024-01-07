using System.Collections;

namespace BeChat.Bencode.Data;

public sealed class BDict : BencodedBase<IDictionary<string, BencodedBase>>, IDictionary<string, BencodedBase>
{
    private SortedDictionary<string, BencodedBase> _dict;

    public BDict()
    {
        _dict = new SortedDictionary<string, BencodedBase>();
    }

    public BDict(IEnumerable<KeyValuePair<string, BencodedBase>> enumerable)
    {
        _dict = new SortedDictionary<string, BencodedBase>(
            enumerable
                .ToDictionary(
                    (kv) => kv.Key, 
                    (kv) => kv.Value
                )
            );
    }

    public override BencodedType Type => BencodedType.Dictionary;
    
    public override ReadOnlyMemory<byte> AsBytes()
    {
        throw new InvalidCastException("Type cannot be casted to bytes");
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
        return this;
    }

    public override string ToString()
    {
        return "";
    }

    public override object Clone()
    {
        return new BDict(this);
    }

    public override IDictionary<string, BencodedBase> Value => _dict;
    
    public int Count => _dict.Count;
    public bool IsReadOnly => false;
    public ICollection<string> Keys => _dict.Keys;
    public ICollection<BencodedBase> Values => _dict.Values;
    public BencodedBase this[string key]
    {
        get => _dict[key];
        set => _dict[key] = value;
    }
    
    public IEnumerator<KeyValuePair<string, BencodedBase>> GetEnumerator()
    {
        return _dict.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(KeyValuePair<string, BencodedBase> item)
    {
        _dict.Add(item.Key, item.Value);
    }

    public void Clear()
    {
        _dict.Clear();
    }

    public bool Contains(KeyValuePair<string, BencodedBase> item)
    {
        return _dict.ContainsKey(item.Key) || _dict.ContainsValue(item.Value);
    }

    public void CopyTo(KeyValuePair<string, BencodedBase>[] array, int arrayIndex)
    {
        _dict.CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, BencodedBase> item)
    {
        return _dict.Remove(item.Key);
    }

    public void Add(string key, BencodedBase value)
    {
        _dict.Add(key, value);
    }

    public void Add(string key, string value)
    {
        _dict.Add(key, new BString(value));
    }

    public void Add(string key, byte[] bytes)
    {
        _dict.Add(key, new BString(bytes));
    }

    public void Add(string key, long value)
    {
        _dict.Add(key, new BInteger(value));
    }

    public void Add(string key, Guid guid)
    {
        _dict.Add(key, new BString(guid.ToByteArray()));
    }
    
    public bool ContainsKey(string key)
    {
        return _dict.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        return _dict.Remove(key);
    }

    public bool TryGetValue(string key, out BencodedBase value)
    {
        return _dict.TryGetValue(key, out value);
    }
}