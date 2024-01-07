using System.Collections;

namespace BeChat.Bencode.Data;

public sealed class BList : BencodedBase<IList<BencodedBase>>, IList<BencodedBase>
{
    private List<BencodedBase> _list;

    public BList()
    {
        _list = new List<BencodedBase>();
    }

    public BList(IEnumerable<BencodedBase> enumerable)
    {
        _list = enumerable.ToList();
    }

    public override BencodedType Type => BencodedType.List;
    
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
        return this;
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
        return new BList(this);
    }

    public override IList<BencodedBase> Value => _list;

    public int Count => _list.Count;
    public bool IsReadOnly => false;
    public BencodedBase this[int index]
    {
        get => _list[index];
        set => _list[index] = value;
    }
    
    public IEnumerator<BencodedBase> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(BencodedBase item)
    {
        _list.Add(item);
    }

    public void Clear()
    {
        _list.Clear();
    }

    public bool Contains(BencodedBase item)
    {
        return _list.Contains(item);
    }

    public void CopyTo(BencodedBase[] array, int arrayIndex)
    {
        _list.CopyTo(array, arrayIndex);
    }

    public bool Remove(BencodedBase item)
    {
        return _list.Remove(item);
    }
    
    public int IndexOf(BencodedBase item)
    {
        return _list.IndexOf(item);
    }

    public void Insert(int index, BencodedBase item)
    {
        _list.Insert(index, item);
    }

    public void RemoveAt(int index)
    {
        _list.RemoveAt(index);
    }
}