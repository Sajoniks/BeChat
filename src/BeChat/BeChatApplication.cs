using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Logging;
using BeChat.Logging.Loggers.DebugLogger;
using BeChat.Logging.Loggers.FileLogger;
using BeChat.Relay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeChat;

public sealed class Bootstrap
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    private static readonly IPEndPoint NullEndPoint = new IPEndPoint(IPAddress.Any, 0);

    private IPEndPoint[] _publicEps = Array.Empty<IPEndPoint>();
    private Dictionary<IPAddress, IPEndPoint> _stunDiscoveredMaps;
    private IPAddress[] _privateIps = Array.Empty<IPAddress>();
    private bool _behindSymmetricNat = false;

    public string UseDevice { get; set; } = "";
    public IReadOnlyCollection<IPAddress> PrivateIps => _privateIps;
    public IReadOnlyCollection<IPEndPoint> PublicEndPoints => _publicEps;
    public IReadOnlyDictionary<IPAddress, IPEndPoint> IPMappings => _stunDiscoveredMaps;

    public Bootstrap(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _stunDiscoveredMaps = new Dictionary<IPAddress, IPEndPoint>(4);
        _logger = logger;
    }

    private async IAsyncEnumerable<IPEndPoint> GetStunServersAsync()
    {
        using var httpClient = new HttpClient();
        string uris = _configuration["Bootstrap:Stun"] ?? throw new ArgumentException();
        string list = await httpClient.GetStringAsync(uris);
        foreach (var str in list.Split('\n'))
        {
            IPAddress? addr = null;
            int port;
            
            try
            {
                var uri = new Uri("udp://" + str);
                var ip = await Dns.GetHostAddressesAsync(uri.Host);
                addr = ip[0];
                port = uri.Port;
            }
            catch (Exception)
            {
                continue;
            }
            
            yield return new IPEndPoint(addr, port);
        }
    }

    private async Task<IPEndPoint> GetPublicEndpointAsync(IPAddress srcIp)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        
        // https://datatracker.ietf.org/doc/html/rfc3489#section-11
        // header

        writer.Write( (ushort) IPAddress.HostToNetworkOrder((short) 0x0001) );
        writer.Write( (ushort)0 );
        for (int i = 0; i < 16; ++i)
        {
            writer.Write((byte)( i + Random.Shared.Next(0, 255)));
        }
        
        var bytes = stream.GetBuffer().AsSpan(0, 20).ToArray();
        
        using var udpClient = new UdpClient(new IPEndPoint(srcIp, 0));
        await foreach (var stunEp in GetStunServersAsync())
        {
            int delay = 100;
            try
            {
                await udpClient.SendAsync(bytes, stunEp);
                
                var cts = new CancellationTokenSource(100);
                var recv = await udpClient.ReceiveAsync(cts.Token);

                if (recv.RemoteEndPoint.Equals(stunEp))
                {
                    // received response

                    using var recvStream = new MemoryStream(recv.Buffer);
                    using var recvReader = new BinaryReader(recvStream);
                    recvStream.Position += 20; // skip header

                    while (recvStream.Position < recvStream.Length)
                    {
                        var attrType = (ushort) IPAddress.NetworkToHostOrder(recvReader.ReadInt16());
                        var attrLen = (ushort) IPAddress.NetworkToHostOrder(recvReader.ReadInt16());
                        if (attrType != 0x0001)
                        {
                            recvStream.Position += attrLen;
                            continue;
                        }

                        var addrFamily = (ushort) IPAddress.NetworkToHostOrder(recvReader.ReadInt16());
                        var addrPort = (ushort) IPAddress.NetworkToHostOrder(recvReader.ReadInt16());
                        var addr = recvReader.ReadUInt32();
                        var ep = new IPEndPoint(addr, addrPort);

                        _logger.LogTrace("STUN response {0} -> {1}", udpClient.Client.LocalEndPoint, ep);

                        {
                            var localEp = udpClient.Client.LocalEndPoint as IPEndPoint ??
                                          throw new InvalidProgramException();

                            if (localEp.Port != ep.Port)
                            {
                                _logger.LogTrace("Received port is not equal to source port ({0} != {1})", localEp.Port, ep.Port);
                                _behindSymmetricNat = true;
                            }
                        }
                        
                        return ep;
                    }
                }
            }
            catch (Exception e)
            {
                if (e is TaskCanceledException || e is OperationCanceledException)
                {
                    delay *= 2;
                    if (delay > 800)
                    {
                        break;
                    }
                }
            }
        }

        return NullEndPoint;
    }

    private async Task<IPAddress[]> GetPrivateIpsAsync()
    {
        var addrList = new List<IPAddress>();
        
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            _logger.LogTrace("====================================================");
            _logger.LogTrace("Discovered {0} network interfaces", interfaces.Length);
            foreach (var ni in interfaces)
            {
                _logger.LogTrace("{0} {1} : {2}", ni.Name, ni.NetworkInterfaceType, ni.OperationalStatus);

                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                // @todo
                if (ni.Name.Contains("ZeroTier"))
                {
                    continue;
                }
          
                if (UseDevice.Length != 0 && !ni.Name.Contains(UseDevice))
                {
                    continue;
                }
                
                switch (ni.NetworkInterfaceType)
                {
                    case NetworkInterfaceType.Wireless80211:
                    case NetworkInterfaceType.Ethernet:
                        foreach (var unicastAddress in ni.GetIPProperties().UnicastAddresses)
                        {
                            _logger.LogTrace("  - {0} ({1})", unicastAddress.Address, unicastAddress.Address.AddressFamily);
                            if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                addrList.Add(unicastAddress.Address);
                            }
                        }
                        break;
                }
            }

            _logger.LogTrace("====================================================");

            return addrList.ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<IPAddress>();
        }
    }

    public async Task DiscoverAsync()
    {
        {
            _logger.LogInfo("Bootstrapping");
            var clock = new Stopwatch();
            clock.Start();

            _privateIps = await GetPrivateIpsAsync();
            {
                _logger.LogTrace("======================= STUN =======================");

                var publicIpList = new List<IPEndPoint>();
                foreach (var ip in _privateIps)
                {
                    try
                    {
                        var ep = await GetPublicEndpointAsync(ip);
                        publicIpList.Add(ep);
                        _stunDiscoveredMaps.Add(ip, ep);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                _publicEps = publicIpList.ToArray();
                _logger.LogTrace("Discovered {0} public IP endpoints", _publicEps.Length);
                
                _logger.LogTrace("====================================================");
            }
            // _upnpAdress = await GetRouterWanIpAsync();
            
            clock.Stop();
            _logger.LogInfo("Bootstrapping done in {0} s.", clock.Elapsed.TotalSeconds);
        }
    }
}

public sealed class BeChatAuth : IAuthorization
{
    public void SetUser(IUser user)
    {
        _currentUser = user;
    }

    public IUser? CurrentUser => _currentUser;

    private IUser? _currentUser;
}

public sealed class BeChatUser : IUser
{
    public Guid Id { get; }
    public string UserName { get; }
    public string Token { get; }

    public bool IsOnline { get; set; }

    public BeChatUser(Guid id, string userName, string token, bool online)
    {
        Id = id;
        UserName = userName;
        Token = token;
        IsOnline = online;
    }
}

public sealed class BeChatNotificationList<T> : ObservableCollection<INotification<T>>, INotificationList<T>
{
    public BeChatNotificationList()
    { }
}

public sealed class BeChatContactList : ObservableCollection<IUser>, IContactList
{
    public BeChatContactList()
    { }
}

public sealed class FriendRequestNotification : INotification<NetNotifyNewInvitation>
{
    public Guid Id { get; init;  } = Guid.Empty;
    public NetNotifyNewInvitation Data { get; init; } = null!;
}

public sealed class ChatConnectNotification : INotification<NetNotifyAcceptConnect>
{
    public Guid Id { get; init;  } = Guid.Empty;
    public NetNotifyAcceptConnect Data { get; init; } = null!;
}

public sealed class BeChatApplication : IApplication, IDisposable, IRelayMessageNotify
{
    public void ReceiveRelayMessage(Response response)
    { }

    [RelayMessageHandler(typeof(NetNotifyNewFriend))]
    public void HandleNewFriend(NetNotifyNewFriend friend)
    {
        lock (_invitations)
        {
            INotification<NetNotifyNewInvitation>? invite =
                _invitations.FirstOrDefault(x => x.Data.UserId.Equals(friend.UserId));
            if (invite is not null)
            {
                _invitations.Remove(invite);
            }
        }

        lock (_contactList)
        {
            _contactList.Add(new BeChatUser(id: friend.UserId, userName: friend.UserName, token: "", online: friend.IsOnline));
        }
    }

    [RelayMessageHandler(typeof(NetNotifyNewInvitation))]
    public void HandleNewInvitation(NetNotifyNewInvitation invitation)
    {
        lock (_contactList)
        {
            if (!_contactList.Any(x => x.Id.Equals(invitation.UserId)))
            {
                lock (_invitations)
                {
                    _invitations.Add(new FriendRequestNotification()
                    {
                        Data = invitation
                    });
                }
            }
        }
    }

    [RelayMessageHandler(typeof(NetNotifyUserOnlineStatus))]
    public void HandleOnlineStatusChanged(NetNotifyUserOnlineStatus notify)
    {
        lock (_contactList)
        {
            IUser? user = _contactList.FirstOrDefault(x => x.Id.Equals(notify.UserId));
            if (user is not null)
            {
                if (user.IsOnline != notify.IsOnline)
                {
                    user.IsOnline = notify.IsOnline;
                    UserPresenceChange?.Invoke(this, user);
                }
            }
        }
    }

    [RelayMessageHandler(typeof(NetNotifyAcceptConnect))]
    public void HandleAcceptConnect(NetNotifyAcceptConnect accept)
    {
        Debug.WriteLine("Accept peer connection [From = {0}  Public = {1}  Private = {2}]", accept.UserId, accept.PublicEp, accept.PrivateEp);

        _connects.Add(new ChatConnectNotification
        {
            Data = accept
        });
    }

    public event EventHandler<IUser>? UserPresenceChange;

    private IServiceProvider? _serviceProvider;
    private ILogger? _logger;
    private IAuthorization _authorization;
    private IConfiguration? _configuration;
    private Bootstrap? _bootstrap;
    private IContactList _contactList;
    private INotificationList<NetNotifyNewInvitation> _invitations;
    private INotificationList<NetNotifyAcceptConnect> _connects;
    private RelayConnection? _relayConnection;

    public IContactList ContactList => _contactList;
    public INotificationList<NetNotifyNewInvitation> Invitations => _invitations;
    public INotificationList<NetNotifyAcceptConnect> Connections => _connects;
    public IServiceProvider Services => _serviceProvider ?? throw new InvalidProgramException();
    public ILogger Logger => _logger ?? throw new InvalidProgramException();
    public IAuthorization Authorization => _authorization;
    public IConfiguration Configuration => _configuration ?? throw new InvalidProgramException();
    public Bootstrap Bootstrap => _bootstrap ?? throw new InvalidProgramException();
    public string Version => _relayConnection?.Version ?? "";

    private BeChatApplication()
    {
        _authorization = new BeChatAuth();
        _contactList = new BeChatContactList();
        _connects = new BeChatNotificationList<NetNotifyAcceptConnect>();
        _invitations = new BeChatNotificationList<NetNotifyNewInvitation>();
    }

    private static BeChatApplication? _app;
    public static BeChatApplication Instance => _app ?? throw new InvalidProgramException();
    
    public static BeChatApplication Create()
    {
        if (_app is not null) throw new InvalidOperationException();
        
        _app = new BeChatApplication();
        return _app;
    }

    private IServiceCollection ConfigureDefaultServices()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(_ => new ConfigurationManager().AddJsonFile("clientProperties.json").Build());
        serviceCollection.AddSingleton<ILoggerFactory>(_ =>
        {
            var logFormatter = new LogFormatter();
            return new LoggerFactory(new ILoggerFactory[]
            {
                new DebugLoggingFactory(logFormatter),
                new FileLoggingFactory(@"logs\bechat", logFormatter)
            });
        });
        serviceCollection.AddSingleton<ILogger>(x =>
        {
            var factory = x.GetRequiredService<ILoggerFactory>();
            return factory.CreateLogger("Client");
        });
        return serviceCollection;
    }

    public RelayConnection CreateRelayConnection()
    {
        string hostName = _configuration?["Relay:Hostname"] ?? "";
        var hostUri = new Uri(hostName);
        var conn = new RelayConnection(hostUri, _logger ?? throw new InvalidProgramException());
        
        conn.AddListener<NetNotifyNewFriend>(this);
        conn.AddListener<NetNotifyNewInvitation>(this);
        conn.AddListener<NetNotifyUserOnlineStatus>(this);
        conn.AddListener<NetNotifyAcceptConnect>(this);
        
        conn.OnDisconnect += (_, _) =>
        {
            _contactList.Clear();
        };
        
        _relayConnection = conn;
        return conn;
    }
    
    public async Task<RelayConnection> ConnectToRelayAsync()
    {
        CreateRelayConnection();
        if (_relayConnection is null) throw new InvalidProgramException();
        
        await _relayConnection.ConnectToRelayAsync(CancellationToken.None);
        return _relayConnection;
    }

    public RelayConnection? Connection => _relayConnection;

    private void GetRequiredServices(IServiceCollection collection)
    {
        _serviceProvider = collection.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configuration = _serviceProvider.GetRequiredService<IConfiguration>();
        _bootstrap = new Bootstrap(_configuration, _logger);
    }
    
    public void ConfigureServices()
    {
        if (_serviceProvider is not null)
        {
            throw new InvalidOperationException();
        }
        
        GetRequiredServices(ConfigureDefaultServices());
    }
    
    public void ConfigureServices(Action<IServiceCollection> action)
    {
        if (_serviceProvider is not null)
        {
            throw new InvalidOperationException();
        }

        var services = ConfigureDefaultServices();
        action(services);

        GetRequiredServices(services);
    }

    public void Dispose()
    {
        _logger?.Dispose();
        _relayConnection?.Dispose();
    }
}