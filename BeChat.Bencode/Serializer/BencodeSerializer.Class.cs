using System.Collections;
using System.Reflection;
using BeChat.Bencode.Data;

namespace BeChat.Bencode.Serializer;

public static partial class BencodeSerializer
{
    private static readonly Dictionary<Type, Type> TypeMapper = new()
    {
        { typeof(BDict), typeof(BDict) },
        { typeof(BList), typeof(BList) },
        { typeof(BInteger), typeof(BInteger) },
        { typeof(BString), typeof(BString) },
        { typeof(long), typeof(BInteger) },
        { typeof(string), typeof(BString) },
        { typeof(byte[]), typeof(BString) },
    };

    private static bool IsBObject(Type type)
    {
        return (type == typeof(BDict)) || 
               (type == typeof(BList)) || 
               (type == typeof(BInteger)) ||
               (type == typeof(BString));
    }

    private static bool IsList(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>);
    }

    private static Type ResolveBObject(object obj)
    {
        var objType = obj.GetType();
        if (TypeMapper.TryGetValue(objType, out var resultType))
        {
            return resultType;
        }
        else
        {
            if (IsList(objType))
            {
                return typeof(BList);
            }
            else
            {
                if (TypeMapper.TryGetValue(objType, out resultType))
                {
                    return resultType;
                }
                else if (objType.IsPrimitive)
                {
                    foreach (var kv in TypeMapper)
                    {
                        if (kv.Key.IsAssignableFrom(objType))
                        {
                            return kv.Value;
                        }
                    }

                    throw new NotSupportedException("Type provided is not supported");
                }
                else
                {
                    return typeof(BDict);
                }
            }
        }
    }
    
    private static BencodedBase MapObjectToBObject(object obj)
    {
        if (IsBObject(obj.GetType()))
        {
            return (obj as ICloneable)?.Clone() as BencodedBase ?? throw new NotSupportedException();
        }
        else
        {
            // root object
            var bObject = Activator.CreateInstance(ResolveBObject(obj)) as BencodedBase ??
                          throw new ArgumentNullException();

            switch (bObject.Type)
            {
                case BencodedType.Integer:
                    var number = (long)obj;
                    return new BInteger(number);

                case BencodedType.String:
                    var str = (string)obj;
                    return new BString(str);

                case BencodedType.Dictionary:
                    var props = obj.GetType().GetProperties();
                    var bDict = new BDict();
                    foreach (var prop in props)
                    {
                        var value = prop.GetValue(obj) ?? throw new ArgumentNullException();
                        var nameAttrib = prop.GetCustomAttribute<BencodePropertyNameAttribute>();
                        string propName = "";
                        if (nameAttrib is null)
                        {
                            propName = prop.Name;
                        }
                        else
                        {
                            propName = nameAttrib.PropName;
                        }

                        bDict.Add(propName, MapObjectToBObject(value));
                    }

                    return bDict;

                case BencodedType.List:
                    var list = (IList)obj;
                    var bList = (BList)bObject;
                    foreach (var listItem in list)
                    {
                        bList.Add(MapObjectToBObject(listItem));
                    }

                    return bList;
            }

            throw new NotSupportedException("Type is not supported");
        }
    }

    public static bool Serialize(BinaryWriter writer, object obj)
    {
        return Serialize(writer, MapObjectToBObject(obj));
    }

    public static byte[] SerializeBytes(object obj)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (Serialize(writer, obj))
        {
            return stream.GetBuffer().AsSpan(0, (int)stream.Length).ToArray();
        }

        return Array.Empty<byte>();
    }
}