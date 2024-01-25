using BeChat.Bencode.Data;
using BeChat.Bencode.Serializer;

namespace BeChat.Common.Protocol.V1;

public sealed class ResponseError : IBencodedPacket
{
    public string Message { get; set; } = "";
    public int ErrorCode { get; set; } = 0;

    public ResponseError()
    { }

    public ResponseError(Exception e, int errorCode)
    {
        Message = e.Message;
        ErrorCode = errorCode;
    }

    public ResponseError(string message, int errorCode)
    {
        Message = message;
        ErrorCode = errorCode;
    }
    
    public BDict BencodedSerialize()
    {
        return new BDict
        {
            { "ex", Message },
            { "code", ErrorCode }
        };
    }

    public void BencodedDeserialize(BDict data)
    {
        Message = data["ex"].ToString();
        ErrorCode = (int) data["code"].AsInteger();
    }
}

public static class ResponseMessageFactory
{
    public static ResponseMessage<T> FromError<T>(ResponseError error) where T : class, IBencodedPacket, new()
    {
        return ResponseMessage<T>.CreateFromError(error);
    }

    public static ResponseMessage<T> FromError<T>(Exception e, int errorCode) where T : class, IBencodedPacket, new()
    {
        return ResponseMessage<T>.CreateFromError(e, errorCode);
    }

    public static ResponseMessage<T> FromError<T>(string errorMessage, int errorCode) where T : class, IBencodedPacket, new()
    {
        return ResponseMessage<T>.CreateFromError(errorMessage, errorCode);
    }
}

public class ResponseMessage<T> : IBencodedPacket where T : class, IBencodedPacket, new()
{
    private bool _isError;
    private ResponseError? _error;
    public bool IsError => _isError;
    public ResponseError? Error => _error;
    public T Content => !_isError ? this as T ?? throw new InvalidProgramException() : throw new NullReferenceException();

    public ResponseMessage()
    {
        _isError = false;
    }
    
    public static ResponseMessage<T> CreateFromError(ResponseError err)
    {
        var inst = new ResponseMessage<T>();
        inst._error = err;
        inst._isError = true;
        return inst;
    }

    public static ResponseMessage<T> CreateFromError(Exception e, int errorCode)
    {
        var inst = new ResponseMessage<T>();
        inst._error = new ResponseError(e, errorCode);
        inst._isError = true;
        return inst;
    }

    public static ResponseMessage<T> CreateFromError(string errorMessage, int errorCode)
    {
        var inst = new ResponseMessage<T>();
        inst._error = new ResponseError(errorMessage, errorCode);
        inst._isError = true;
        return inst;
    }


    protected virtual BDict Serialize() { throw new NotImplementedException(); }
    protected virtual void Deserialize(BDict data) { throw new NotImplementedException(); }

    public BDict BencodedSerialize()
    {
        if (IsError)
        {
            return new BDict
            {
                { "t", "e" },
                { "bd", _error!.BencodedSerialize() }
            };
        }
        else
        {
            var result = new BDict
            {
                { "t", "r" },
                { "bd", Serialize() }
            };

            return result;
        }
    }

    public void BencodedDeserialize(BDict data)
    {
        var t = data["t"].ToString();
        if (t.Equals("e"))
        {
            var content = data["bd"] as BDict ?? throw new NullReferenceException();
            _error = new ResponseError();
            _error.BencodedDeserialize(content);
            _isError = true;
        }
        else if (t.Equals("r"))
        {
            _isError = false;
            _error = null;
            
            var content = data["bd"] as BDict ?? throw new NullReferenceException();
            Deserialize(content);
        }
        else
        {
            throw new InvalidDataException();
        }
    }

    protected static BDict OK()
    {
        return new BDict
        {
            { "op", 0 }
        };
    }
}