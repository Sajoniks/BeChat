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
    public RegisterResponse() { }

    protected override BDict Serialize()
    {
        return OK();
    }

    protected override void Deserialize(BDict data)
    { }
}