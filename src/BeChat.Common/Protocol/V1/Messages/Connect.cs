using System.Net;
using System.Net.Sockets;
using BeChat.Bencode.Data;
using BeChat.Common.Entity;

namespace BeChat.Common.Protocol.V1.Messages;

public sealed class RoomPeer
{
    public IPEndPoint PublicEndpoint { get; }
    public IPEndPoint UpnpEndPoint { get; }
    public IPEndPoint PrivateEndpoint { get; }
    public Socket? Socket { get; }
    public string PeerName { get; }

    public RoomPeer(string name, Socket? socket, IPEndPoint publicEp, IPEndPoint upnpEp, IPEndPoint privateEp)
    {
        PeerName = name;
        Socket = socket;
        PublicEndpoint = publicEp;
        UpnpEndPoint = upnpEp;
        PrivateEndpoint = privateEp;
    }
}

public sealed class ConnectIpList
{
    public IReadOnlyCollection<IPEndPoint> PublicEndpoints { get; set; } = null!;
    public IReadOnlyCollection<IPEndPoint> PrivateEndpoints { get; set; } = null!;

    public IEnumerable<IPEndPoint> GetEndPoints()
    {
        var items = PublicEndpoints.Concat(PrivateEndpoints);
        foreach (var item in items)
        {
            yield return item;
        }
    }
}

public sealed class ConnectRequest : RequestMessage<ConnectRequest>
{
    private ConnectIpList? _ipList;
    private string _roomName = "";
    private string _hostName = "";

    public string RoomName => _roomName;
    public string UserName => _hostName;
    public ConnectIpList? IpList => _ipList;

    static ConnectRequest()
    {
        RequestName = "connect";
    }
    
    public ConnectRequest()
    { }

    public ConnectRequest(string roomName, string hostName, ConnectIpList list) : this()
    {
        _roomName = roomName;
        _hostName = hostName;
        _ipList = list;
    }

    protected override BDict Serialize()
    {
        if (_ipList is null) throw new InvalidDataException();
        
        var content = new BDict();
        
        content.Add("pub", BeChatStreamExtensions.WriteCompactEndpoints(_ipList.PublicEndpoints.ToArray()));
        content.Add("priv", BeChatStreamExtensions.WriteCompactEndpoints(_ipList.PrivateEndpoints.ToArray()));
        
        content.Add("host", _hostName);
        content.Add("room", _roomName);
        return content;
    }

    protected override void Deserialize(BDict data)
    {
        _ipList = new ConnectIpList()
        {
            PublicEndpoints = BeChatStreamExtensions.ParseCompactEndpoints(data["pub"].AsBytes()),
            PrivateEndpoints = BeChatStreamExtensions.ParseCompactEndpoints(data["priv"].AsBytes())
        };

        _roomName = data["room"].ToString();
        _hostName = data["host"].ToString();
    }

    protected override bool HasBody => true;
}

public sealed class ConnectResponse : ResponseMessage<ConnectResponse>
{
    private Guid _roomId = Guid.Empty;
    private IRoomPeer? _peer;
    private ConnectIpList? _ipList;

    public Guid RoomId => _roomId;
    public IRoomPeer? Peer => _peer;
    public ConnectIpList? IpList => _ipList;

    public ConnectResponse() { }

    public ConnectResponse(Guid roomId)
    {
        _roomId = roomId;
    }

    public ConnectResponse(Guid roomId, IRoomPeer peer, ConnectIpList ipList)
    {
        _roomId = roomId;
        _peer = peer;
        _ipList = ipList;
    }

    protected override BDict Serialize()
    {
        var result = new BDict();
        result.Add("id", _roomId);
        
        if (_peer is not null)
        {
            if (_ipList is null) throw new InvalidDataException();
            
            var peerDict = new BDict
            {
                { "n", _peer.UserName },
            };
            
            peerDict.Add("pub", BeChatStreamExtensions.WriteCompactEndpoints(_ipList.PublicEndpoints.ToArray()));
            peerDict.Add("priv", BeChatStreamExtensions.WriteCompactEndpoints(_ipList.PrivateEndpoints.ToArray()));
            result.Add("peer", peerDict);
        }

        return result;
    }

    protected override void Deserialize(BDict data)
    {
        _roomId = data["id"].AsGuid();
        if (data.ContainsKey("peer"))
        {
            var peerDict = data["peer"] as BDict ?? throw new InvalidDataException();

            _ipList = new ConnectIpList()
            {
                PublicEndpoints = BeChatStreamExtensions.ParseCompactEndpoints(peerDict["pub"].AsBytes()).ToArray(),
                PrivateEndpoints = BeChatStreamExtensions.ParseCompactEndpoints(peerDict["priv"].AsBytes()).ToArray()
            };

            _peer = new RemoteRoomPeer(peerDict["n"].ToString());
        }
    }
}