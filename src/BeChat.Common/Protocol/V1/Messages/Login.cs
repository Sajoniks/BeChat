using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1.Messages;

public sealed class LoginRequest : RequestMessage<LoginRequest>
{
    public LoginRequest() { }
    
    public LoginRequest(string userName, string password)
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
        throw new NotImplementedException();
    }

    protected override void Deserialize(BDict data)
    {
        throw new NotImplementedException();
    }

    protected override bool HasBody => true;
}

public sealed class LoginResponse : ResponseMessage<LoginResponse>
{
    public LoginResponse() { }
    
    protected override BDict Serialize()
    {
        return new BDict
        {
            { "tok", "token" }
        };
    }

    protected override void Deserialize(BDict data)
    { }
}