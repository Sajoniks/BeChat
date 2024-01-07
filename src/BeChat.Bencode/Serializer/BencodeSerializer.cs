using System.Text;
using BeChat.Bencode.Data;

namespace BeChat.Bencode.Serializer;

// API 
// Serialize -> Accepts BReader
// Deserialize -> Accepts BWriter
// BReader, BWriter are initialized from some source

public class BencodeSerializeException : Exception
{ }

public static partial class BencodeSerializer
{
    private abstract class BencodedState
    {
        public abstract void AddState(BencodedState state);
        public abstract BencodedBase GetObject();
    }

    private class BencodedListState : BencodedState
    {
        private List<BencodedBase> _list;

        public BencodedListState()
        {
            _list = new List<BencodedBase>();
        }
        
        public override void AddState(BencodedState state)
        {
            _list.Add(state.GetObject());
        }

        public override BencodedBase GetObject()
        {
            return new BList(_list);
        }
    }

    private class BencodedDictState : BencodedState
    {
        private List<KeyValuePair<string, BencodedBase>> _kvList;
        private Stack<BencodedState> _stack;

        public BencodedDictState()
        {
            _kvList = new List<KeyValuePair<string, BencodedBase>>();
            _stack = new Stack<BencodedState>();
        }
        
        public override void AddState(BencodedState state)
        {
            _stack.Push(state);
            if (_stack.Count == 2)
            {
                BencodedBase value = _stack.Pop().GetObject();
                BencodedBase key = new BString( _stack.Pop().GetObject().AsBytes() );
                
                _kvList.Add(new KeyValuePair<string, BencodedBase>(key.ToString(), value));
            }
        }

        public override BencodedBase GetObject()
        {
            return new BDict(_kvList);
        }
    }

    private class BencodedIntState : BencodedState
    {
        private long _item;
        public BencodedIntState(long item)
        {
            _item = item;
        }
        
        public override void AddState(BencodedState state)
        {
            throw new InvalidOperationException("Integer is not a container");
        }

        public override BencodedBase GetObject()
        {
            return new BInteger();
        }
    }

    private class BencodedStringState : BencodedState
    {
        private byte[] _stringBytes;

        public BencodedStringState(byte[] buffer)
        {
            _stringBytes = buffer;
        }
        
        public override void AddState(BencodedState state)
        {
            throw new InvalidOperationException("String is not a container");
        }

        public override BencodedBase GetObject()
        {
            return new BString(_stringBytes);
        }
    }
    
    public static bool Serialize(BinaryWriter writer, BencodedBase bobject)
    {
        bool serialized = false;

        switch (bobject.Type)
        {
            case BencodedType.Integer:
                long value = bobject.AsInteger();
                writer.Write('i');
                SerializeHelpers.ByteSerializeInt64(writer.BaseStream, value);
                writer.Write('e');
                serialized = true;
                break;
            
            case BencodedType.String:
                var span = bobject.AsBytes();
                SerializeHelpers.ByteSerializeInt64(writer.BaseStream, span.Length);
                writer.Write(':');
                writer.Write(span.Span);
                serialized = true;
                break;
            
            case BencodedType.List:
                serialized = true;
                writer.Write('l');
                foreach (var listItem in bobject.AsList())
                {
                    serialized &= Serialize(writer, listItem);
                }
                writer.Write('e');
                break;
            
            case BencodedType.Dictionary:
                serialized = true;
                writer.Write('d');
                foreach (var dictItem in bobject.AsDictionary())
                {
                    var keyBytes = Encoding.UTF8.GetBytes(dictItem.Key);
                    SerializeHelpers.ByteSerializeInt64(writer.BaseStream, keyBytes.LongLength);
                    writer.Write(':');
                    writer.Write(keyBytes);
                    
                    serialized &= Serialize(writer, dictItem.Value);
                }
                writer.Write('e');
                break;
        }

        return serialized;
    }

    public static BencodedBase Deserialize(BinaryReader reader)
    {
        BencodedBase? resultObject = null;
        Stack<BencodedState> stateStack = new();

        var readNumber = (char start) =>
        {
            var sb = new StringBuilder();
            sb.Append(start);
            start = reader.ReadChar();
            while (start != ':' && start != 'e')
            {
                sb.Append(start);
                start = reader.ReadChar();
            }

            return Int64.Parse(sb.ToString());
        };
        
        while (reader.BaseStream.CanRead)
        {
            char b = reader.ReadChar();
            if (b.Equals('i'))
            {
                b = reader.ReadChar();
                long number = readNumber(b);

                if (stateStack.Any())
                {
                    stateStack.Peek().AddState(new BencodedIntState(number));
                }
                else
                {
                    resultObject = new BInteger();
                    break;
                }
            }
            else if (b.Equals('l'))
            {
                stateStack.Push(new BencodedListState());
            }
            else if (b.Equals('d'))
            {
                stateStack.Push(new BencodedDictState());
            }
            else if (b.Equals('e'))
            {
                BencodedState prevObject = stateStack.Pop();
                if (stateStack.Any())
                {
                    stateStack.Peek().AddState(prevObject);
                }
                else
                {
                    resultObject = prevObject.GetObject();
                    break;
                }
            }
            else if (Char.IsNumber(b))
            {
                long size = readNumber(b);
                var buffer = new byte[size];
                
                for (int i = 0; i < size; ++i)
                {
                    buffer[i] = reader.ReadByte();
                }

                if (stateStack.Any())
                {
                    stateStack.Peek().AddState(new BencodedStringState(buffer));
                }
                else
                {
                    resultObject = new BString(buffer);
                    break;
                }
            }
        }

        return resultObject ?? throw new BencodeSerializeException();
    }

    public static T Deserialize<T>(BinaryReader reader) where T : BencodedBase
    {
        return Deserialize(reader) as T ?? throw new BencodeSerializeException();
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> buffer) where T : BencodedBase
    {
        unsafe
        {
            fixed (byte* b = &buffer.GetPinnableReference())
            {
                using var stream = new UnmanagedMemoryStream(b, buffer.Length);
                using var reader = new BinaryReader(stream);
                return Deserialize<T>(reader);
            }
        }
    }

    public static T Deserialize<T>(ArraySegment<byte> buffer) where T : BencodedBase
    {
        if (buffer.Array is null) throw new ArgumentNullException(nameof(buffer));
        
        using var stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count);
        using var reader = new BinaryReader(stream);
        return Deserialize<T>(reader);
    }

    public static T Deserialize<T>(byte[] buffer) where T : BencodedBase
    {
        return Deserialize<T>(new ArraySegment<byte>(buffer));
    }
}