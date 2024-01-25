using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1.Messages;

public sealed class WelcomeRequest : RequestMessage<WelcomeRequest>
{
    private string _clientVersion;

    static WelcomeRequest()
    {
        RequestName = "welcome";
    }
    
    public WelcomeRequest()
    {
        _clientVersion = "1.0.0";
    }

    protected override BDict Serialize()
    {
        return new BDict
        {
            { "ver", _clientVersion }
        };
    }

    protected override void Deserialize(BDict data)
    {
        _clientVersion = data["ver"].ToString();
    }

    protected override bool HasBody => true;
    
    public string ClientVersion => _clientVersion;
}

public sealed class WelcomeResponse : ResponseMessage<WelcomeResponse>
{
    private string _actualVersion = "";

    public string ActualVersion => _actualVersion;

    public WelcomeResponse()
    { }

    public WelcomeResponse(string actualVersion)
    {
        _actualVersion = actualVersion;
    }

    protected override BDict Serialize()
    {
        return new BDict
        {
            { "ver", _actualVersion }
        };
    }

    protected override void Deserialize(BDict data)
    {
        _actualVersion = data["ver"].ToString();
    }
}