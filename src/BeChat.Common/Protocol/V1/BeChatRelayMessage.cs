using System.Buffers.Binary;
using System.Text;
using BeChat.Bencode.Data;

namespace BeChat.Common.Protocol.V1;

[NetMessage]
public sealed class NetMessageAck
{ }

[NetMessage("welcome")]
public sealed class NetMessageWelcome
{
    [NetMessageProperty("ver")]
    public string Version { get; init; } = ""; 
}

[NetMessage("connect")]
public sealed class NetMessageConnect
{
    
}

public abstract class NetMessageWithToken
{
    [NetMessageProperty("tok")]
    public string Token { get; init; } = "";
}

[NetMessage("add-contact")]
public sealed class NetMessageAddContact : NetMessageWithToken
{
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
}

[NetMessage("accept-contact")]
public sealed class NetMessageAcceptContact : NetMessageWithToken
{
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
}

[NetMessage("find-contacts")]
public sealed class NetMessageFindContacts : NetMessageWithToken
{
    [NetMessageProperty("q")]
    public string QueryString { get; init; } = "";
}

public class NetMessageContact
{
    [NetMessageProperty("usr")]
    public string UserName { get; init; } = "";
    
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
}

[NetMessage("new-invitation")]
public sealed class NetNotifyNewInvitation : NetMessageContact { }

[NetMessage("new-friend")]
public sealed class NetNotifyNewFriend : NetMessageContact { }

[NetMessage]
public sealed class NetMessageFindContactsList
{
    [NetMessageProperty("r")]
    public List<NetMessageContact> Contacts { get; init; } = new();
}

[NetMessage("login")]
public sealed class NetMessageLogin
{
    [NetMessageProperty("usr")]
    public string UserName { get; init; } = "";
    
    [NetMessageProperty("pw")]
    public string Password { get; init; } = "";
}

[NetMessage("auto-login")]
public sealed class NetMessageAutoLogin
{
    [NetMessageProperty("tok")]
    public string Token { get; init; } = "";
}

[NetMessage("register")]
public sealed class NetMessageRegister
{
    [NetMessageProperty("usr")]
    public string UserName { get; init; } = "";
    
    [NetMessageProperty("pw")]
    public string Password { get; init; } = "";
}

[NetMessage]
public sealed class NetMessageUserData
{
    [NetMessageProperty("usr")]
    public string UserName { get; init; } = "";
    
    [NetMessageProperty("tok")]
    public string Token { get; init;  } = "";
}