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
        { typeof(Guid), typeof(BString) }
    };

    public static bool IsBObject(Type type)
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

    private static class PropertyUtilits
    {
        public static Y? GetPropertyValue<Y>(Type type, PropertyInfo prop, object inst)
        {
            var getter = prop.GetGetMethod(nonPublic: true);
            if (getter is not null)
            {
                return (Y?)getter.Invoke(inst, null); 
            }
            else
            {
                var actualProp = type.GetField($"<{prop.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (actualProp is not null)
                {
                    return (Y?)actualProp.GetValue(inst);
                }
                else
                {
                    throw new InvalidProgramException();
                }
            }
        }
        
        public static void SetPropertyValue(Type type, PropertyInfo prop, object inst, object? value)
        {
            var setter = prop.GetSetMethod(nonPublic: true);
            if (setter is not null)
            {
                setter.Invoke(inst, new[] { value });
            }
            else
            {
                var actualProp = type.GetField($"<{prop.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (actualProp is not null)
                {
                    actualProp.SetValue( inst, value );
                }
                else
                {
                    throw new InvalidProgramException();
                }
            }
        }
    }

    public static object? MapBDictToObject(BDict dict, Type objectType)
    {
        try
        {
            var inst = Activator.CreateInstance(objectType) ?? throw new NullReferenceException();
            MapBDictToObject(dict, inst);
            return inst;
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public static void MapBDictToObject(BDict dict, object obj)
    {
        var type = obj.GetType();
        var props = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (!dict.ContainsKey(prop.Name))
            {
                continue;
            }

            BencodedBase bencoded = dict[prop.Name];
            
            var propTypeCode = Type.GetTypeCode(prop.PropertyType);
            switch (propTypeCode)
            {
                case TypeCode.Byte:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (byte) bencoded.AsInteger());
                    break;
                
                case TypeCode.SByte:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (sbyte) bencoded.AsInteger());
                    break;
                
                case TypeCode.Boolean:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, bencoded.AsInteger() != 0);
                    break;

                case TypeCode.Int16:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (short) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt16:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (ushort) bencoded.AsInteger());
                    break;
                
                case TypeCode.Int32:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (int) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt32:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (uint) bencoded.AsInteger());
                    break;
                
                case TypeCode.Int64:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (long) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt64:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, (ulong) bencoded.AsInteger());
                    break;
                
                case TypeCode.String:
                    PropertyUtilits.SetPropertyValue(type, prop, obj, bencoded.ToString());
                    break;
                
                case TypeCode.Object:
                    {
                        if (bencoded.Type == BencodedType.Dictionary)
                        {
                            var inst = Activator.CreateInstance(prop.PropertyType);
                            if (inst is not null)
                            {
                                MapBDictToObject(dict, inst);
                            }
                        }
                        else if (bencoded.Type == BencodedType.String)
                        {
                            var bytes = bencoded.AsBytes();
                            
                            if (prop.PropertyType == typeof(Guid))
                            {
                                PropertyUtilits.SetPropertyValue(type, prop, obj, new Guid(bytes.Span));
                            }
                            else if (prop.PropertyType.IsArray &&
                                     prop.PropertyType.GenericTypeArguments[0] == typeof(byte))
                            {
                                PropertyUtilits.SetPropertyValue(type, prop, obj, bytes.ToArray());
                            }
                        }
                    }
                    break;
            }
        }
    }
    
    public static BencodedBase MapObjectToBObject(object obj)
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
                    {
                        if (obj is string)
                        {
                            return new BString((string) obj);
                        }
                        else if (obj is Guid)
                        {
                            return new BString(((Guid)obj).ToByteArray());
                        }
                    }
                    break;

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