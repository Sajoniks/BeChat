using System.Buffers.Binary;
using System.Collections;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using BeChat.Bencode.Data;
using BeChat.Bencode.Serializer;

namespace BeChat.Common.Protocol;


public sealed class NetMessageParseException : Exception
{
    public NetMessageParseException() { }
    public NetMessageParseException(string s) : base(s) { }
}

public sealed class NetMessageReader : IDisposable
{
    private bool _disposed = false;
    private readonly BDict _source;

    public NetMessageReader(BDict content)
    {
        _source = content;
    }

    public NetMessageReader(ReadOnlySpan<byte> buffer)
    {
        _source = BencodeSerializer.Deserialize<BDict>(buffer);
    }

    public byte[] ReadBlob(string key)
    {
        var bytes = _source[key].AsBytes().ToArray();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    public long ReadInt64(string key)
    {
        return _source[key].AsInteger();
    }

    public int ReadInt32(string key)
    {
        return (int)_source[key].AsInteger();
    }
    
    public short ReadInt16(string key)
    {
        return (short)_source[key].AsInteger();
    }

    public string ReadString(string key)
    {
        return _source[key].ToString();
    }

    public object? ReadObject(string key, Type type)
    {
        if (BencodeSerializer.IsBObject(type))
        {
            return _source[key];
        }
        else
        {

            try
            {
                var dict = _source[key] as BDict ?? throw new NullReferenceException();
                var inst = BencodeSerializer.MapBDictToObject(dict, type);
                return inst;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public IList ReadList(string key, Type dataType)
    {
        var typeCode = Type.GetTypeCode(dataType);
        var listType = typeof(List<>).MakeGenericType(dataType);
        var result = Activator.CreateInstance(listType) as IList ?? throw new NullReferenceException();
        
        var list = _source[key].AsList();

        foreach (var bencoded in list)
        {
            switch (typeCode)
            {
                case TypeCode.Byte:
                    result.Add((byte) bencoded.AsInteger());
                    break;
                
                case TypeCode.SByte:
                    result.Add((sbyte) bencoded.AsInteger());
                    break;
                    
                case TypeCode.Boolean:
                    result.Add(bencoded.AsInteger() != 0);
                    break;
                    
                case TypeCode.Int16:
                    result.Add((short) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt16:
                    result.Add((ushort) bencoded.AsInteger());
                    break;
                
                case TypeCode.Int32:
                    result.Add((int) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt32:
                    result.Add((uint) bencoded.AsInteger());
                    break;
                
                case TypeCode.Int64:
                    result.Add((long) bencoded.AsInteger());
                    break;
                
                case TypeCode.UInt64:
                    result.Add((ulong) bencoded.AsInteger());
                    break;
                
                case TypeCode.String:
                    result.Add(bencoded.ToString());
                    break;

                case TypeCode.Object:
                    {
                        if (bencoded.Type == BencodedType.Dictionary)
                        {
                            var dict = (bencoded as BDict)!;
                            var inst = BencodeSerializer.MapBDictToObject(dict, dataType);
                            if (inst is not null)
                            {
                                result.Add(inst);
                            }
                        }
                    }
                    break;
            }
        }

        return result;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

public sealed class NetMessageWriter : IDisposable
{
    private bool _disposed = false;
    private readonly BDict _source;
    
    public NetMessageWriter()
    {
        _source = new BDict();
    }

    public void WriteBlob(string key, ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        
        _source.Add(key, bytes);
    }
    
    public void WriteInt64(string key, long data)
    {
        _source.Add(key, data);
    }
    
    public void WriteInt32(string key, int data)
    {
        _source.Add(key, data);
    }
    
    public void WriteInt16(string key, short data)
    {
        _source.Add(key, data);
    }

    public void WriteString(string key, string data)
    {
        _source.Add(key, data);
    }

    public void WriteObject(string key, object obj)
    {
        _source.Add(key, BencodeSerializer.MapObjectToBObject(obj));
    }

    public void WriteList(string key, IList data, Type dataType)
    {
        var list = new BList();
        var typeCode = Type.GetTypeCode(dataType);

        foreach (var obj in data)
        {
            if (obj is null)
            {
                continue;
            }

            switch (typeCode)
            {
                case TypeCode.String:
                    list.Add(new BString( (string)obj ));
                    break;
                
                case TypeCode.Byte:
                    list.Add(new BInteger( (byte)obj ));
                    break;
                
                case TypeCode.SByte:
                    list.Add(new BInteger( (sbyte)obj ));
                    break;
                
                case TypeCode.Boolean:
                    list.Add(new BInteger( ((bool)obj) ? 1 : 0 ));
                    break;
                
                case TypeCode.Int16:
                    list.Add(new BInteger( (short)obj ));
                    break;
                
                case TypeCode.UInt16:
                    list.Add(new BInteger( (ushort)obj ));
                    break;
                
                case TypeCode.Int32:
                    list.Add(new BInteger( (int)obj ));
                    break;
                
                case TypeCode.UInt32:
                    list.Add(new BInteger( (uint)obj ));
                    break;
                    
                case TypeCode.Int64:
                    list.Add(new BInteger( (long)obj ));
                    break;
                
                case TypeCode.UInt64:
                    list.Add(new BInteger( (long)(ulong)obj ));
                    break;
                
                case TypeCode.Object:
                    list.Add( BencodeSerializer.MapObjectToBObject(obj) );
                    break;
            }
        }
        _source.Add(key, list);
    }

    public void CopyTo(Stream stream)
    {
        stream.Write(_source.SerializeBytes());
    }

    public BDict Data => _source;
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

[AttributeUsage(validOn: AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NetMessageAttribute : Attribute
{
    public NetMessageAttribute(string identifier)
    {
        Id = identifier;
    }

    public NetMessageAttribute()
    {
        Id = "";
    }

    public string Id { get; }
}

[AttributeUsage(validOn: AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class NetMessageSerializerAttribute : Attribute
{ }

[AttributeUsage(validOn: AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class NetMessageDeserializerAttribute : Attribute
{ }

[AttributeUsage(validOn: AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NetMessagePropertyAttribute : Attribute
{
    public NetMessagePropertyAttribute(string? shortName, bool skipDefault = false)
    {
        ShortName = shortName;
        SkipDefault = skipDefault;
    }

    public string? ShortName { get; }
    public bool SkipDefault { get; }
}

public sealed class Request
{
    private readonly BDict _requestBody;
    private readonly string _name;
    private uint? _sequenceNumber;

    public Request(string name, BDict content)
    {
        _name = name;
        if (_name.Length == 0) throw new ArgumentException("Name must not be empty");

        _requestBody = new BDict
        {
            { "t", "q" },
            { "q", name },
            { "bd", content }
        };
    }

    public string Name => _name;

    private Request(BDict request)
    {
        _name = request["q"].ToString();
        if (_name.Length == 0) throw new ArgumentException("Name must not be empty");
        
        _requestBody = request;

        if (_requestBody.ContainsKey("s"))
        {
            _sequenceNumber = (uint) _requestBody["s"].AsInteger();
        }
    }

    public T ReadContent<T>() where T : new()
    {
        using var reader = new NetMessageReader(_requestBody["bd"] as BDict ?? throw new NullReferenceException("Request must have a body"));
        return NetMessage<T>.Read(reader);
    }

    public byte[] GetBytes()
    {
        return _requestBody.SerializeBytes();
    }
    
    public static Request FromMessage<T>(T message) where T : new()
    {
        using var writer = new NetMessageWriter();
        NetMessage<T>.Write(message, writer);
        return new Request(NetMessage<T>.GetMessageId(), writer.Data);
    }

    public static Request FromBytes(ReadOnlySpan<byte> buffer)
    {
        var bdict = BencodeSerializer.Deserialize<BDict>(buffer);
        return new Request(bdict);
    }

    public Response CreateResponse<T>(T content) where T : new()
    {
        var writer = new NetMessageWriter();
        NetMessage<T>.Write(content, writer);
        return new Response(this, writer.Data);
    }

    public Response CreateError(string errorMessage)
    {
        return new Response(this, errorMessage);
    }

    public Response CreateError(Exception e)
    {
        return new Response(this, e);
    }

    public bool HasSequence => _sequenceNumber is not null;
    
    public uint Sequence
    {
        get => _sequenceNumber ?? throw new InvalidOperationException();
        set
        {
            if (_sequenceNumber is null)
            {
                _sequenceNumber = value;
                _requestBody.Add("s", value);
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
    }
}

[NetMessage]
public sealed class ResponseError
{
    [NetMessageProperty("m")]
    public string Message { get; init;  } = "";
}


public sealed class Response
{
    private readonly BDict _response;
    private readonly bool _error;
    private readonly string _requestName;
    private uint? _sequenceNumber;

    public Response(Request request, Exception e) : this(request, e.Message)
    {}
    
    public Response(string requestName, Exception e) : this(requestName, e.Message)
    { }

    public Response(Request request, string errorMessage) : this(request.Name, errorMessage)
    {
        if (request.HasSequence)
        {
            Sequence = request.Sequence;
        }
    }
    
    public Response(string requestName, string errorMessage)
    {
        _requestName = requestName;
        if (_requestName.Length == 0) throw new ArgumentException("Request name must not be empty");
        _error = true;

        _response = new BDict
        {
            { "t", "e" },
            { "q", _requestName },
            { "bd", new BDict { { "m", errorMessage} } }
        };
    }

    public Response(Request request, BDict responseBody) : this(request.Name, responseBody)
    {
        if (request.HasSequence)
        {
            Sequence = request.Sequence;
        }
    }

    private Response(string requestName, BDict responseBody, bool error)
    {
        _requestName = requestName;
        if (_requestName.Length == 0) throw new ArgumentException("Request name must not be empty");
        _error = error;

        if (error)
        {
            _response = new BDict
            {
                { "t", "e" },
                { "q", _requestName },
                { "bd", responseBody }
            };
        }
        else
        {
            _response = new BDict
            {
                { "t", "r" },
                { "q", _requestName },
                { "bd", responseBody }
            };
        }
    }
    
    public Response(string requestName, BDict responseBody)
    {
        _requestName = requestName;
        if (_requestName.Length == 0) throw new ArgumentException("Request name must not be empty");
        _error = false;
        
        _response = new BDict
        {
            { "t", "r" },
            { "q", _requestName },
            { "bd", responseBody }
        };
    }

    public Response(BDict response)
    {
        _requestName = response["q"].ToString();
        if (_requestName.Length == 0) throw new ArgumentException("Name must not be empty");
        
        if (response["t"].ToString().Equals("e"))
        {
            _error = true;
        }

        if (response.ContainsKey("s"))
        {
            _sequenceNumber = (uint)response["s"].AsInteger();
        }

        _response = response;
    }

    public string RequestName => _requestName;
    public bool IsError => _error;

    private void ThrowInInvalidMessage(Type t)
    {
        if (IsError && t != typeof(ResponseError))
            throw new InvalidOperationException("Error can be read only from ResponseError class");
    }
    
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="NetMessageParseException"></exception>
    public object? ReadContent(Type type)
    {
        ThrowInInvalidMessage(type);

        using var reader = new NetMessageReader(_response["bd"] as BDict ?? throw new NetMessageParseException("Non error response must have body"));
        var obj = NetMessage.ReadObject(type, reader);
        return obj;
    } 
    
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="NetMessageParseException"></exception>
    public T ReadContent<T>() where T : new()
    {
        ThrowInInvalidMessage(typeof(T));
        
        using var reader = new NetMessageReader(_response["bd"] as BDict ?? throw new NetMessageParseException("Non error response must have body"));
        var obj = NetMessage<T>.Read(reader);
        return obj;
    }

    public static Response CreateGenericResponse<T>(T msg) where T : new()
    {
        return CreateGenericResponse(NetMessage<T>.GetMessageId(), msg);
    }
    
    public static Response CreateGenericResponse<T>(string name, T msg) where T : new()
    {
        using var writer = new NetMessageWriter();
        NetMessage<T>.Write(msg, writer);
        var r= new Response(name, writer.Data, typeof(T) == typeof(ResponseError));
        return r;
    }

    public static Response CreateGenericResponse<T>(object message) where T : new()
    {
        using var writer = new NetMessageWriter();
        NetMessage.WriteObject(message, writer);
        var r = new Response(NetMessage<T>.GetMessageId(), writer.Data, typeof(T) == typeof(ResponseError));
        return r;
    }
    
    public ResponseError ReadError()
    {
        if (!IsError)
            throw new InvalidOperationException("Response is not an error");

        return ReadContent<ResponseError>();
    }

    public byte[] GetBytes()
    {
        return _response.SerializeBytes();
    }

    public bool HasSequence => _sequenceNumber is not null;

    public uint Sequence
    {
        get => _sequenceNumber ?? throw new InvalidOperationException();
        set
        {
            if (_sequenceNumber is null)
            {
                _sequenceNumber = value;
                _response.Add("s", value);
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
    }
}

public static class NetMessage
{
    private static class PropertyUtility
    {
        public static Y? GetPropertyValue<Y>(Type type, PropertyInfo prop, object inst)
        {
            var getter = prop.GetGetMethod(nonPublic: true);
            if (getter is not null)
            {
                return (Y?)getter.Invoke(inst, null);
;            }
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

        public static bool IsInt64(Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.UInt64:
                case TypeCode.Int64:
                    return true;
                default:
                    return false;
            }
        }
        
        public static bool IsInt32(Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.UInt32:
                case TypeCode.Int32:
                    return true;
                default:
                    return false;
            }
        }
        
        public static bool IsInt16(Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.UInt16:
                case TypeCode.Int16:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsBool(Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean:
                    return true;
            }

            return false;
        }
    }

    public static string GetMessageId(Type type)
    {
        var attr = type.GetCustomAttribute<NetMessageAttribute>()!;
        return attr.Id;
    }
    
    /// <exception cref="NetMessageParseException">Thrown if failed to instantiate object of type Type</exception>
    public static object ReadObject(Type type, NetMessageReader reader)
    {
        try
        {
            var inst = Activator.CreateInstance(type) ?? throw new NullReferenceException();
            return ReadObject(inst, type, reader);
        }
        catch (Exception)
        {
            throw new NetMessageParseException();
        }
    }
        
    public static object ReadObject(object inst, Type type, NetMessageReader reader)
    {
        var props = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            var propType = prop.PropertyType;
            var attribute = prop.GetCustomAttribute<NetMessagePropertyAttribute>();

            string propName = prop.Name;
            bool skipDefault = false;
            if (attribute is not null)
            {
                if (attribute.ShortName is not null)
                {
                    propName = attribute.ShortName;
                }

                skipDefault = attribute.SkipDefault;
            }
            
            if (Type.GetTypeCode(propType) == TypeCode.String)
            {
                var str = reader.ReadString(propName);
                if (skipDefault && str.Equals(default))
                {
                    continue;
                }
                PropertyUtility.SetPropertyValue(type, prop, inst, str);
            }
            else if (PropertyUtility.IsInt64(propType))
            {
                var val = reader.ReadInt64(propName);
                PropertyUtility.SetPropertyValue(type, prop, inst, val);
            }
            else if (PropertyUtility.IsInt32(propType))
            {
                var val = reader.ReadInt32(propName);
                PropertyUtility.SetPropertyValue(type, prop, inst, val);
            }
            else if (PropertyUtility.IsInt16(propType))
            {
                var val = reader.ReadInt16(propName);
                PropertyUtility.SetPropertyValue(type, prop, inst, val);
            }
            else if (PropertyUtility.IsBool(propType))
            {
                var val = reader.ReadInt16(propName) != 0;
                PropertyUtility.SetPropertyValue(type, prop, inst, val);
            }
            else if (propType.IsArray && (Type.GetTypeCode(propType.GetElementType()) == TypeCode.Byte))
            {
                var bytes = reader.ReadBlob(propName);
                PropertyUtility.SetPropertyValue(type, prop, inst, bytes);
            }
            else if (!propType.IsClass)
            {
                if (propType == typeof(Guid))
                {
                    var bytes = reader.ReadBlob(propName);
                    PropertyUtility.SetPropertyValue(type, prop, inst, new Guid(bytes));
                }
            }
            else
            {
                bool isPlainClass = true;
                
                foreach (var @interface in propType.GetInterfaces())
                {
                    if (@interface == typeof(IList) || (@interface.IsGenericType &&
                                                        @interface.GetGenericTypeDefinition() == typeof(IList<>)))
                    {
                        var list = reader.ReadList(propName, propType.GenericTypeArguments[0]);
                        PropertyUtility.SetPropertyValue(type, prop, inst, list);
                        isPlainClass = false;
                        break;
                    }
                    else if (@interface == typeof(IDictionary) || (@interface.IsGenericType &&
                                                                   @interface.GetGenericTypeDefinition() == typeof(IDictionary<,>)))

                    {
                        isPlainClass = false;
                        break;
                    }
                }

                if (isPlainClass && propType.IsClass && propType != typeof(Delegate))
                {
                    if (propType == typeof(DateTime))
                    {
                        var ticks = reader.ReadInt64(propName);
                        PropertyUtility.SetPropertyValue(type, prop, inst, new DateTime(ticks));
                    }
                    else if (propType == typeof(IPEndPoint))
                    {
                        var bytes = reader.ReadBlob(propName);
                        uint ip = 0;
                        ushort port = 0;

                        ip = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                        port = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
                                
                        PropertyUtility.SetPropertyValue(type, prop, inst, new IPEndPoint(ip, port));
                    }
                    else
                    {
                        var methods = propType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                        MethodInfo? deserializer = null;
                        foreach (var method in methods)
                        {
                            if (method.GetCustomAttribute<NetMessageDeserializerAttribute>() is not null)
                            {
                                deserializer = method;
                                break;
                            }
                        }

                        var propObj = reader.ReadObject(propName, propType);
                        if (deserializer is not null)
                        {
                            var bencodedObj = deserializer.Invoke(null, new[] { propObj });
                            PropertyUtility.SetPropertyValue(type, prop, inst, bencodedObj);
                        }
                        else
                        {
                            PropertyUtility.SetPropertyValue(type, prop, inst, propObj);
                        }
                    }
                }
            }
        }

        return inst;
    }
    
    public static void WriteObject(Object obj, NetMessageWriter writer)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        var type = obj.GetType();
        var props = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var propType = prop.PropertyType;
            var attribute = prop.GetCustomAttribute<NetMessagePropertyAttribute>();

            string propName = prop.Name;
            bool skipDefault = false;
            if (attribute is not null)
            {
                if (attribute.ShortName is not null)
                {
                    propName = attribute.ShortName;
                }

                skipDefault = attribute.SkipDefault;
            }
            
            if (Type.GetTypeCode(propType) == TypeCode.String)
            {
                var str = PropertyUtility.GetPropertyValue<string>(type, prop, obj) ?? "";
                if (skipDefault && str.Equals(default))
                {
                    continue;
                }
                writer.WriteString(propName, str);
            }
            else if (PropertyUtility.IsInt64(propType))
            {
                var val = PropertyUtility.GetPropertyValue<long>(type, prop, obj);
                writer.WriteInt64(propName, val);
            }
            else if (PropertyUtility.IsInt32(propType))
            {
                var val = PropertyUtility.GetPropertyValue<int>(type, prop, obj);
                writer.WriteInt32(propName, val);
            }
            else if (PropertyUtility.IsInt16(propType))
            {
                var val = PropertyUtility.GetPropertyValue<short>(type, prop, obj);
                writer.WriteInt16(propName, val);
            }
            else if (PropertyUtility.IsBool(propType))
            {
                var val = PropertyUtility.GetPropertyValue<bool>(type, prop, obj);
                writer.WriteInt16(propName, val ? (short)1 : (short)0);
            }
            else if (propType.IsArray && (Type.GetTypeCode(propType.GetElementType()) == TypeCode.Byte))
            {
                var val = PropertyUtility.GetPropertyValue<byte[]>(type, prop, obj) ?? Array.Empty<byte>();
                writer.WriteBlob(propName, val);
            }
            else if (!propType.IsClass)
            {
                if (propType == typeof(Guid))
                {
                    var val = PropertyUtility.GetPropertyValue<Guid>(type, prop, obj);
                    writer.WriteBlob(propName, val.ToByteArray());
                }
            }
            else
            {
                bool isPlainClass = true;
                
                foreach (var @interface in propType.GetInterfaces())
                {
                    if (@interface == typeof(IList) || (@interface.IsGenericType &&
                                                        @interface.GetGenericTypeDefinition() == typeof(IList<>)))
                    {
                        // parse list
                        IList? list = PropertyUtility.GetPropertyValue<IList>(type, prop, obj);
                        if (list is not null)
                        {
                            writer.WriteList(propName, list, propType.GenericTypeArguments[0]);
                            isPlainClass = false;
                            break;
                        }
                    }
                    else if (@interface == typeof(IDictionary) || (@interface.IsGenericType &&
                                                              @interface.GetGenericTypeDefinition() == typeof(IDictionary<,>)))

                    {
                        isPlainClass = false;
                        break;
                    }
                }

                if (isPlainClass && propType.IsClass && propType != typeof(Delegate))
                {
                    if (propType == typeof(DateTime))
                    {
                        var val = PropertyUtility.GetPropertyValue<DateTime>(type, prop, obj);
                        writer.WriteInt64(propName, val.Ticks);
                    }
                    else if (propType == typeof(IPEndPoint))
                    {
                        var val = PropertyUtility.GetPropertyValue<IPEndPoint>(type, prop, obj);
                        if (val is null)
                        {
                            val = new IPEndPoint(0, 0);
                        }
                        var ipBuffer = new byte[6];
                        BinaryPrimitives.WriteUInt32BigEndian(ipBuffer, BitConverter.ToUInt32(val.Address.GetAddressBytes()));
                        BinaryPrimitives.WriteUInt16BigEndian(ipBuffer.AsSpan(4), (ushort) val.Port);
                        writer.WriteBlob(propName, ipBuffer);
                    }
                    else
                    {
                        var methods = propType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                        MethodInfo? serializer = null;
                        foreach (var method in methods)
                        {
                            if (method.GetCustomAttribute<NetMessageSerializerAttribute>() is not null)
                            {
                                serializer = method;
                                break;
                            }
                        }

                        var propObj = PropertyUtility.GetPropertyValue<object>(type, prop, obj);
                        if (serializer is not null)
                        {
                            var encoded = serializer.Invoke(null, new[] { propObj });
                            if (encoded is not null)
                            {
                                writer.WriteObject(propName, encoded);
                            }
                        }
                        else
                        {
                            if (propObj is not null)
                            {
                                writer.WriteObject(propName, propObj);
                            }
                        }
                    }
                }
            }
        }
    }
}

public static class NetMessage<T> where T : new()
{
    static NetMessage()
    {
        var type = typeof(T);
        bool attributeFound = type.GetCustomAttribute<NetMessageAttribute>() is not null;
        if (!attributeFound)
        {
            throw new ArgumentException("T must have NetMessage attribute");
        }
    }
    
    public static string GetMessageId()
    {
        return NetMessage.GetMessageId(typeof(T));
    }

    public static T Read(NetMessageReader reader)
    {
        var inst = new T();
        NetMessage.ReadObject(inst, typeof(T), reader);
        return inst;
    }

    public static void Write(T obj, NetMessageWriter writer)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        NetMessage.WriteObject(obj, writer);
    }
}