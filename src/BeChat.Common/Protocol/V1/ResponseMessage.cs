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
        return Activator.CreateInstance(typeof(ResponseMessage<T>), error) as ResponseMessage<T> ??
               throw new InvalidProgramException();
    }

    public static ResponseMessage<T> FromError<T>(Exception e, int errorCode) where T : class, IBencodedPacket, new()
    {
        return Activator.CreateInstance(typeof(ResponseMessage<T>), e, errorCode) as ResponseMessage<T> ??
               throw new InvalidProgramException();
    }

    public static ResponseMessage<T> FromError<T>(string errorMessage, int errorCode) where T : class, IBencodedPacket, new()
    {
        return Activator.CreateInstance(typeof(ResponseMessage<T>), errorMessage, errorCode) as ResponseMessage<T> ??
               throw new InvalidProgramException();
    }
}

public abstract class ResponseMessage<T> : IBencodedPacket where T : class, IBencodedPacket, new()
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
    
    public ResponseMessage(ResponseError err)
    {
        _error = err;
        _isError = true;
    }

    public ResponseMessage(Exception e, int errorCode)
    {
        _error = new ResponseError(e, errorCode);
        _isError = true;
    }

    public ResponseMessage(string errorMessage, int errorCode)
    {
        _error = new ResponseError(errorMessage, errorCode);
        _isError = true;
    }


    protected abstract BDict Serialize();
    protected abstract void Deserialize(BDict data);

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