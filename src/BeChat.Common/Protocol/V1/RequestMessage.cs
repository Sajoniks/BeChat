using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1;

public abstract class RequestMessage<T> : IBencodedPacket where T : RequestMessage<T>, new()
{
    public static string RequestName { get; protected set; } = "";
    
    public RequestMessage()
    { }

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
        
        if (t.Equals("q") && q.Equals(RequestName) && HasBody)
        {
            if (data.TryGetValue("bd", out var body))
            {
                var content = body as BDict ?? throw new NullReferenceException();
                Deserialize(content);
            }
        }
    }
}