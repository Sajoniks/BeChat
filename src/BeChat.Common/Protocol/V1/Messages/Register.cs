using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1.Messages;

public sealed class RegisterRequest : RequestMessage<RegisterRequest>
{
    static RegisterRequest()
    {
        RequestName = "register";
    }
    
    public RegisterRequest(){ }

    public RegisterRequest(string userName, string password) : this()
    {
        _username = userName;
        _password = password;
    }

    private string _username = "";
    private string _password = "";

    public string UserName => _username;
    public string Password => _password;
    
    protected override BDict Serialize()
    {
        return new BDict
        {
            { "usr", _username },
            { "pw", _password }
        };
    }

    protected override void Deserialize(BDict data)
    {
        _username = data["usr"].ToString();
        _password = data["pw"].ToString();
    }

    protected override bool HasBody => true;
}

public sealed class RegisterResponse : ResponseMessage<RegisterResponse>
{
    private string _token = "";
    private string _username = "";
    
    public RegisterResponse() { }

    public string Token => _token;
    public string UserName => _username;

    public RegisterResponse(string token, string userName)
    {
        _token = token;
        _username = userName;
    }
    
    protected override BDict Serialize()
    {
        return new BDict
        {
            { "tok", _token },
            { "usr", _username }
        };
    }

    protected override void Deserialize(BDict data)
    {
        _token = data["tok"].ToString();
        _username = data["usr"].ToString();
    }
}