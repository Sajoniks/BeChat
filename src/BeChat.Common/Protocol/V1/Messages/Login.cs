using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1.Messages;

public sealed class LoginRequest : RequestMessage<LoginRequest>
{
    static LoginRequest()
    {
        RequestName = "login";
    }
    
    public LoginRequest() { }
    
    public LoginRequest(string userName, string password)
    {
        _username = userName;
        _password = password;
    }

    public LoginRequest(string token)
    {
        _token = token;
    }
    
    private string _username = "";
    private string _password = "";
    private string _token = "";

    public string UserName => _username;
    public string Password => _password;
    public string Token => _token;
    
    protected override BDict Serialize()
    {
        if (_token.Length == 0)
        {
            return new BDict
            {
                { "usr", _username },
                { "pw", _password }
            };
        }
        else
        {
            return new BDict
            {
                { "tok", _token }
            };
        }
    }

    protected override void Deserialize(BDict data)
    {
        if (data.ContainsKey("tok"))
        {
            _token = data["tok"].ToString();
        }
        else
        {
            _username = data["usr"].ToString();
            _password = data["pw"].ToString();
        }
    }

    protected override bool HasBody => true;
}

public sealed class LoginResponse : ResponseMessage<LoginResponse>
{
    private string _token = "";
    private string _username = "";
    
    public LoginResponse() { }

    public string Token => _token;
    public string UserName => _username;
    
    public LoginResponse(string token, string userName)
    {
        _token = token;
        _username = userName;
    }
    
    protected override BDict Serialize()
    {
        return new BDict
        {
            { "usr", _username },
            { "tok", _token }
        };
    }

    protected override void Deserialize(BDict data)
    {
        _token = data["tok"].ToString();
        _username = data["usr"].ToString();
    }
}