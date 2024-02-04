using System.Buffers.Binary;
using System.Net;
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
public sealed class NetMessageConnect : NetMessageWithToken
{
    [NetMessageProperty("id")]
    public Guid ConnectToId { get; init; } = Guid.Empty;
    
    [NetMessageProperty("prip")]
    public IPEndPoint PrivateIp { get; init; } = null!;
    
    [NetMessageProperty("pubip")]
    public IPEndPoint PublicIp { get; init; } = null!;
}

[NetMessage("accept-connect")]
public sealed class NetMessageAcceptConnect : NetMessageWithToken
{
    [NetMessageProperty("id")] 
    public Guid ConnectId { get; init; } = Guid.Empty;
    
    [NetMessageProperty("prip")]
    public IPEndPoint PrivateIp { get; init; } = null!;
    
    [NetMessageProperty("pubip")]
    public IPEndPoint PublicIp { get; init; } = null!;
}

[NetMessage("new-accept-connect")]
public sealed class NetNotifyAcceptConnect
{
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
    
    [NetMessageProperty("prip")]
    public IPEndPoint PrivateEp { get; init; } = null!;
    
    [NetMessageProperty("pubip")]
    public IPEndPoint PublicEp { get; init; } = null!;
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
    
    [NetMessageProperty("ls")]
    public DateTime LastSeen { get; init; } = DateTime.MinValue;
    
    [NetMessageProperty("online")]
    public bool IsOnline { get; init; } = false;
}

[NetMessage("new-invitation")]
public sealed class NetNotifyNewInvitation : NetMessageContact { }

[NetMessage("new-friend")]
public sealed class NetNotifyNewFriend : NetMessageContact
{ }

[NetMessage("new-conn-request")]
public sealed class NetNotifyConnectRequest
{ }


[NetMessage("online-status")]
public sealed class NetNotifyUserOnlineStatus
{
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
    
    [NetMessageProperty("online")]
    public bool IsOnline { get; init; } = false;
}

[NetMessage]
public sealed class NetResponseContactsList
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

[NetMessage("get-contacts")]
public sealed class NetMessageGetContacts : NetMessageWithToken
{ }

[NetMessage("is-online")]
public sealed class NetMessageIsOnline : NetMessageWithToken
{
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
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
public sealed class NetResponseIsOnline
{
    [NetMessageProperty("val")]
    public bool IsOnline { get; init; }
}

[NetMessage]
public sealed class NetContentLoginRegister
{
    [NetMessageProperty("usr")]
    public string UserName { get; init; } = "";
    
    [NetMessageProperty("id")]
    public Guid UserId { get; init; } = Guid.Empty;
    
    [NetMessageProperty("tok")]
    public string Token { get; init;  } = "";
}