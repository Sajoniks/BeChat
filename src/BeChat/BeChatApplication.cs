using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

public sealed class BeChatApplication : IApplication, IDisposable
{
    private IServiceProvider? _serviceProvider;
    private ILogger? _logger;
    private IConfiguration? _configuration;
    private Bootstrap? _bootstrap;
    private RelayConnection? _relayConnection;

    public IServiceProvider Services => _serviceProvider ?? throw new InvalidProgramException();
    public ILogger Logger => _logger ?? throw new InvalidProgramException();
    public IConfiguration Configuration => _configuration ?? throw new InvalidProgramException();
    public Bootstrap Bootstrap => _bootstrap ?? throw new InvalidProgramException();
    public string Version => _relayConnection?.Version ?? ""
    ;
    private BeChatApplication()
    {
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

    public async Task<RelayConnection> ConnectToRelayAsync()
    {
        string hostName = _configuration?["Relay:Hostname"] ?? "";
        var hostUri = new Uri(hostName);

        _relayConnection = new RelayConnection(hostUri, _logger ?? throw new InvalidProgramException());
        await _relayConnection.ConnectToRelayAsync(CancellationToken.None);
        return _relayConnection;
    }

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