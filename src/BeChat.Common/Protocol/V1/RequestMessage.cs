using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1;

public abstract class RequestMessage<T> : IBencodedPacket where T : RequestMessage<T>, new()
{
    public static string RequestName { get; protected set; } = "";
    private string _requestName;

    public RequestMessage()
    {
        _requestName = RequestName;
    }

    protected abstract BDict Serialize();
    protected abstract void Deserialize(BDict data);
    protected abstract bool HasBody { get; }
    
    public BDict BencodedSerialize()
    {
        var result = new BDict
        {
            { "t", "q" },
            { "q", RequestName },
        };
        if (HasBody)
        {
            result.Add("bd", Serialize());
        }

        return result;
    }

    public void BencodedDeserialize(BDict data)
    {
        var t = data["t"].ToString();
        var q = data["q"].ToString();
        if (!t.Equals("q") || !q.Equals(_requestName))
        {
            throw new InvalidDataException("Tried to deserialize wrong type of request");
        }
        if (HasBody)
        {
            var content = data["bd"] as BDict ??
                          throw new NullReferenceException(
                              "Expected to have content in request that HasBody equals true");
            Deserialize(content);
        }
    }
}