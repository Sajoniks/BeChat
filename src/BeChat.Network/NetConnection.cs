using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using NSec.Cryptography;
using Timer = System.Threading.Timer;

namespace BeChat.Network;

/// <summary>
///
/// <c>NetConnection</c> is a wrapper over UDP socket that provides a degree of reliability
/// 
/// </summary>
public sealed class NetConnection : IDisposable
{
    enum PacketType
    {
        Seq,
        Enk,
        EnkAck,
        Ack,
        KeepAlive
    }

    struct Header
    {
        public Header() { }

        /// <summary>
        /// Reads structure from span. Data in span must be written in Big Endian order
        /// </summary>
        public Header(ReadOnlySpan<byte> b)
        {
            if ((uint)b.Length < (uint)PacketHeaderFields.HeaderSize)
            {
                throw new ArgumentException();
            }
            
            ProtocolId = BinaryPrimitives.ReadUInt32BigEndian(b);
            PacketType = (PacketType)BinaryPrimitives.ReadUInt32BigEndian(b.Slice(4));
            Checksum   = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8));
            PacketId   = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(12));
            Ack        = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(16));
        }
        
        public uint ProtocolId { get; set; } = 0;
        public uint PacketId { get; set; } = 0;
        public PacketType PacketType { get; set; } = 0;
        public uint Checksum { get; set; } = 0;
        public uint Ack { get; set; } = 0;

        /// <summary>
        /// Writes structure to stream in Big Endian order
        /// </summary>
        public void WriteBytes(Stream s)
        {
            Span<byte> buffer = stackalloc byte[4];
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, ProtocolId);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint) PacketType);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Checksum);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, PacketId);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Ack);
            s.Write(buffer);
        }
        
        public enum PacketHeaderFields
        {
            ProtocolId = sizeof(uint),
            PacketType = sizeof(uint),
            Checksum = sizeof(uint),
            PacketNum = sizeof(uint),
            Ack = sizeof(uint),
        
            HeaderSize = ProtocolId + PacketType + Checksum + PacketNum + Ack
        }

        public static int HeaderSize => (int) PacketHeaderFields.HeaderSize;
    }
    record BufferedPacket(ArraySegment<byte> Data, Header Header);

    private sealed class Window
    {
        public enum State
        {
            Blocked,
            Up
        }

        public enum Mode
        {
            Receiver,
            Sender
        }
        
        private readonly int _windowSize;
        
        private State _state = State.Up;
        private Mode _mode;

        private uint _nextPacket = 0;
        private uint _base = 0;

        private SortedSet<uint> _packetReceivedAcks = new();
        private SortedSet<uint> _packetsWaitingAck = new();
        private Queue<uint> _availablePackets = new();

        public uint NextPacketId => _nextPacket;
        public uint BasePacketId => _base;

        public bool IsEmpty => !_packetsWaitingAck.Any();
        public bool IsReady => _state == State.Up;
        public bool IsBlocked => _state == State.Blocked;
        public Queue<uint> AvailablePackets => _availablePackets;
        public IReadOnlySet<uint> OutstandingPackets => _packetsWaitingAck;

        public Window(Mode mode, int windowSize)
        {
            _windowSize = windowSize;
            _mode = mode;
        }

        public bool IsInWindow(uint packetId)
        {
            return packetId >= _base && packetId < _base + _windowSize;
        }

        public void Push()
        {
            if (_mode != Mode.Sender)
            {
                throw new NotSupportedException();
            }
            
            if ((_nextPacket - _base + 1) >= _windowSize)
            {
                _state = State.Blocked;
            }

            _packetsWaitingAck.Add(_nextPacket);
            _nextPacket++;
        }

        public int Put(uint packetId)
        {
            if (packetId < _base || packetId >= (_base + _windowSize))
            {
                return 0;
            }
            
            if (_packetReceivedAcks.Add(packetId))
            {
                _packetsWaitingAck.Remove(packetId);
                
                if (_base == packetId)
                {
                    // shift window 

                    int shifted = 0;
                    while (_packetReceivedAcks.Any() && _packetReceivedAcks.First() == _base)
                    {
                        uint curPktId = _packetReceivedAcks.First();
                        ++_base;
                        ++shifted;
                        
                        _availablePackets.Enqueue(curPktId);
                        _packetReceivedAcks.Remove(curPktId);
                    }

                    return shifted;
                }
            }

            return 0;
        }
    }
    
    private readonly uint _protocolId;
    private readonly Socket _socket;

    private volatile bool _connected = false;
    private volatile EndPoint? _remoteEp;

    public bool Connected => _connected;
    public EndPoint? LocalEndPoint => _socket.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _remoteEp;
    public Socket Socket => _socket;
    
    private readonly int _windowSize = 5;

    private readonly Thread _socketThread;
    private readonly System.Timers.Timer _timer;
    private readonly System.Timers.Timer _keepAliveTimer;
    
    private readonly Window _sendWindow;
    private readonly Window _recvWindow;

    private readonly Dictionary<uint, BufferedPacket> _bufferedInPackets = new();
    private readonly Dictionary<uint, BufferedPacket> _bufferedOutPackets = new();
    private readonly Queue<Memory<byte>> _packets = new();
    private readonly ManualResetEventSlim _packetsAvailableResetEvent = new();

    private long _lastPacketTimeReceived = 0;
    
    private readonly byte[] _recvBuffer = new byte[16 * 1024 * 1024];
    private int _recvPos = 0;
    private readonly byte[] _sendBuffer = new byte[16 * 1024 * 1024];
    private int _sendPos = 0;
    
    private bool _disposed = false;

    private Key? _key;
    private SharedSecret? _secret;


    public NetConnection(uint protocolId)
    {
        _protocolId = protocolId;
        _socketThread = new Thread(SocketReceiveThread);
        _socketThread.Name = "Net Connection Thread";
        
        _timer = new System.Timers.Timer();
        _timer.Interval = 1000;
        _timer.AutoReset = false;
        _timer.Elapsed += OnTimeout;

        _keepAliveTimer = new System.Timers.Timer();
        _keepAliveTimer.Interval = 2000;
        _keepAliveTimer.AutoReset = true;
        _keepAliveTimer.Elapsed += KeepAliveTimerOnElapsed;
        
        _sendWindow = new Window(Window.Mode.Sender, _windowSize);
        _recvWindow = new Window(Window.Mode.Receiver, _windowSize);
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    private void KeepAliveTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_remoteEp is null)
        {
            return;
        }

        var h = new Header
        {
            ProtocolId = _protocolId,
            PacketType = PacketType.KeepAlive,
            PacketId = _sendWindow.NextPacketId
        };
        
        Send(h, new byte[1]);
        
        Debug.WriteLine("NetConnection sent keep-alive packet [{0} => {1}]", LocalEndPoint, RemoteEndPoint);
    }

    private void OnTimeout(object? sender, ElapsedEventArgs e)
    {
        if (_remoteEp is null)
        {
            throw new SocketException();
        }
        
        lock (_sendBuffer)
        {
            foreach (uint packetId in _sendWindow.OutstandingPackets)
            {
                if (_bufferedOutPackets.TryGetValue(packetId, out var packet))
                {
                    _socket.SendTo(_sendBuffer, packet.Data.Offset, packet.Data.Count, SocketFlags.None, _remoteEp);
                }
            }

            if (_bufferedOutPackets.Count > 0)
            {
                _timer.Start();
            }
        }
    }

    private void DisconnectFromReceiveThread()
    {
        Debug.WriteLine("Connection time-out");

        _connected = false;
        _socket.Dispose();

        _keepAliveTimer.Dispose();
        _timer.Dispose();
    }

    private void SocketReceiveThread()
    {
        if (_remoteEp is null || !_connected || _secret is null)
        {
            Debug.WriteLine("Stopping thread {0} due connection is not established", Thread.CurrentThread.Name);
        }
        
        EndPoint ep = new IPEndPoint(0, 0);
        while (_connected)
        {
            if (!_socket.Poll(0, SelectMode.SelectRead))
            {
                var lastTime = new DateTime(_lastPacketTimeReceived);
                var timespan = TimeSpan.FromSeconds(10);
                if (Debugger.IsAttached)
                {
                    timespan = TimeSpan.FromDays(1);
                }
                
                if (DateTime.UtcNow - lastTime > timespan)
                {
                    DisconnectFromReceiveThread();
                    return;
                }
                
                continue;
            }

            int recvPacketLen = 0;
            try
            {
                recvPacketLen = _socket.ReceiveFrom(_recvBuffer, _recvPos, _recvBuffer.Length - _recvPos, SocketFlags.None, ref ep);
                if (!ep.Equals(_remoteEp))
                {
                    continue;
                }
            }
            catch (Exception)
            {
                continue;
            }

            _lastPacketTimeReceived = DateTime.UtcNow.Ticks;

            if (recvPacketLen >= Header.HeaderSize)
            {
                int curRecvPos = _recvPos;
                
                Header h;
                try
                {
                    h = new Header(_recvBuffer.AsSpan(curRecvPos));
                    if (h.ProtocolId != _protocolId)
                    {
                        continue;
                    }
                    curRecvPos += Header.HeaderSize;
                    recvPacketLen -= Header.HeaderSize;
                }
                catch (Exception)
                {
                    continue;
                }

                switch (h.PacketType)
                {
                    case PacketType.Seq:
                    case PacketType.KeepAlive:
                        {
                            lock (_recvWindow)
                            {
                                Debug.WriteLine("NetConnection packet [type = {0}  src = {1}  len = {2}]",
                                    h.PacketType, 
                                    _remoteEp,
                                    recvPacketLen  
                                );
                                
                                int availablePackets = 0;
                                if (_recvWindow.IsInWindow(h.PacketId))
                                {
                                    var segment = new ArraySegment<byte>(_recvBuffer, curRecvPos, recvPacketLen);
                                    var packet = new BufferedPacket(segment, h);
                                    _bufferedInPackets[h.PacketId] = packet;
                                    availablePackets = _recvWindow.Put(h.PacketId);
                                }

                                int prevPacketsNum;
                                int newPacketsNum;
                                lock (_packets)
                                {
                                    prevPacketsNum = _packets.Count;
                                    
                                    if (availablePackets > 0)
                                    {
                                        // @todo
                                        using var key = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(_secret,
                                            ReadOnlySpan<byte>.Empty, new byte[12], AeadAlgorithm.Aes256Gcm);
                                        while (_recvWindow.AvailablePackets.TryDequeue(out var packetId))
                                        {
                                            if (_bufferedInPackets.Remove(packetId, out var packet))
                                            {
                                                Span<byte> cipherPacket =
                                                    _recvBuffer.AsSpan(packet.Data.Offset, packet.Data.Count);
                                                
                                                byte[]? content = AeadAlgorithm.Aes256Gcm.Decrypt(key, new byte[12],
                                                    new byte[12], cipherPacket);
                                                Array.Fill(_recvBuffer, Byte.MinValue, packet.Data.Offset,
                                                    packet.Data.Count);

                                                if (packet.Data.Offset + packet.Data.Count == _recvPos)
                                                {
                                                    // we have reached end of the buffer
                                                    _recvPos = 0;
                                                }

                                                if (content is null)
                                                {
                                                    continue;
                                                }

                                                if (h.PacketType != PacketType.KeepAlive)
                                                {
                                                    _packets.Enqueue(content);
                                                }
                                            }
                                        }
                                    }
                                    
                                    newPacketsNum = _packets.Count;
                                }

                                if (prevPacketsNum != newPacketsNum && prevPacketsNum == 0)
                                {
                                    _packetsAvailableResetEvent.Set();
                                }
                            }
                            
                            // send ack
                            
                            var ackHeader = new Header
                            {
                                ProtocolId = _protocolId,
                                Ack = h.PacketId,
                                PacketId = h.PacketId,
                                PacketType = PacketType.Ack
                            };

                            try
                            {
                                using var ackMemoryStream = new MemoryStream(new byte[Header.HeaderSize]);
                                ackHeader.WriteBytes(ackMemoryStream);
                                _socket.SendTo(ackMemoryStream.ToArray(), Header.HeaderSize, SocketFlags.None, _remoteEp);
                            }
                            catch (Exception)
                            {
                                continue;
                            }
                        }
                        break;

                    case PacketType.Ack:
                        {
                            lock (_sendBuffer)
                            {
                                if (_sendWindow.IsInWindow(h.Ack))
                                {
                                    int available = _sendWindow.Put(h.Ack);
                                    if (available > 0)
                                    {
                                        while (_sendWindow.AvailablePackets.TryDequeue(out uint packetId))
                                        {
                                            if (_bufferedOutPackets.Remove(packetId, out var packet))
                                            {
                                                Array.Fill(_sendBuffer, Byte.MinValue, packet.Data.Offset,
                                                    packet.Data.Count);
                                                if (packet.Data.Offset + packet.Data.Count == _sendPos)
                                                {
                                                    // we have reached end of the send buffer
                                                    _sendPos = 0;
                                                }
                                            }
                                        }
                                    }
                                }

                                if (_bufferedOutPackets.Count == 0)
                                {
                                    _timer.Stop();
                                    _timer.Start();
                                }
                            }
                        }
                        break;
                }
            }
        }
    }

    public void Bind(IPEndPoint endPoint, bool reuseSocket = false)
    {
        if (reuseSocket)
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }
        _socket.Bind(endPoint);
    }

    public async Task ConnectAsync(IPEndPoint endPoint, CancellationToken token)
    {
        var sl = new SpinLock();

        bool connectionEstablished = false;
        
        bool sentEncryption = false;
        bool receivedEncryption = false;
        
        bool sentEncryptionAck = false;
        bool receivedEncryptionAck = false;
        
        byte[] keyBuffer = Array.Empty<byte>();

        bool retryPacket = false;

        System.Timers.Timer timer = new();
        timer.Interval = 2000;
        timer.AutoReset = true;
        timer.Elapsed += (_, _) =>
        {
            bool locked = false;
            try
            {
                sl.TryEnter(ref locked);
                retryPacket = true;
            }
            finally
            {
                if (locked) sl.Exit();
            }
        };
        timer.Start();

        byte[] packetBuffer = new byte[128];
        using MemoryStream packetStream = new MemoryStream(packetBuffer, 0, packetBuffer.Length, writable: true, publiclyVisible: true);
        using BinaryReader packetReader = new BinaryReader(packetStream);
        using BinaryWriter packetWriter = new BinaryWriter(packetStream);

        static async Task<bool> SendPacket(Socket socket, MemoryStream packetStream, EndPoint ep, CancellationToken innerToken)
        {
            try
            {
                var segment = new ArraySegment<byte>(packetStream.GetBuffer(), 0, (int)packetStream.Position);
                await socket.SendToAsync(segment, SocketFlags.None, ep, innerToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException e)
            {
                if (innerToken.IsCancellationRequested)
                {
                    throw;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        Task<bool> SendProtocol()
        {
            packetStream.Position = 0;
            var protocolId = (uint) IPAddress.HostToNetworkOrder((int) _protocolId);
            packetWriter.Write(protocolId);
            
            Debug.WriteLine("NetConnection send protocol packet [{0} -> {1}]", LocalEndPoint, endPoint);
            return SendPacket(_socket, packetStream, endPoint, token);
        }

        Task<bool> SendEncryption()
        {
            packetStream.Position = 0;
            bool locked = false;
            try
            {
                sl.TryEnter(ref locked);
                    
                // send encryption
                if (!sentEncryption)
                {
                    _key = new Key(KeyAgreementAlgorithm.X25519);
                    keyBuffer = _key.Export(KeyBlobFormat.RawPublicKey);
                }

                var header = new Header
                {
                    PacketType = PacketType.Enk,
                    ProtocolId = _protocolId
                };

                packetStream.Position = 0;
                header.WriteBytes(packetStream);
                packetWriter.Write(IPAddress.HostToNetworkOrder(keyBuffer.Length));
                packetWriter.Write(keyBuffer);
            }
            finally
            {
                if (locked) sl.Exit();
            }

            Debug.WriteLine("NetConnection send encryption packet [{0} -> {1}]", LocalEndPoint, endPoint);
            return SendPacket(_socket, packetStream, endPoint, token);
        }

        Task<bool> SendEncryptionAck()
        {
            packetStream.Position = 0;
            Header responseH = new Header
            {
                ProtocolId = _protocolId,
                PacketType = PacketType.EnkAck
            };
            responseH.WriteBytes(packetStream);

            Debug.WriteLine("NetConnection send encryption ack packet [{0} -> {1}]", LocalEndPoint, endPoint);
            return SendPacket(_socket, packetStream, endPoint, token);
        }

        try
        {
            await SendProtocol();
        }
        catch (Exception)
        {
            return;
        }

        _lastPacketTimeReceived = DateTime.UtcNow.Ticks;
        
        while (!connectionEstablished)
        {
            token.ThrowIfCancellationRequested();
            packetStream.Position = 0;
            
            if (retryPacket)
            {
                try
                {
                    if (!receivedEncryption)
                    {
                        await SendProtocol();
                    }
                    else
                    {
                        if (!receivedEncryptionAck)
                        {
                            await SendEncryption();
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    continue;
                }
                retryPacket = false;
            }

            if (!_socket.Poll(0, SelectMode.SelectRead))
            {
                var lastTime = new DateTime(_lastPacketTimeReceived);
                continue;
            }

            EndPoint recvFromEp;
            int recvPktLen = 0;
            try
            {
                SocketReceiveFromResult result = await _socket
                    .ReceiveFromAsync(packetBuffer, SocketFlags.None, endPoint, token)
                    .ConfigureAwait(false);

                recvPktLen = result.ReceivedBytes;
                recvFromEp = result.RemoteEndPoint;

                Debug.WriteLine("NetConnection received packet [{0} <- {1}]", LocalEndPoint, recvFromEp);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                continue;
            }

            if (!recvFromEp.Equals(endPoint))
            {
                Debug.WriteLine("NetConnection mismatch packet srt [{0}, expected {1}]", recvFromEp, endPoint);
                continue;
            }

            _lastPacketTimeReceived = DateTime.UtcNow.Ticks;
            
            if (recvPktLen == sizeof(UInt32)) // protocol received
            {
                uint recvProtocol = (uint) IPAddress.NetworkToHostOrder(packetReader.ReadInt32());
                if (recvProtocol != _protocolId)
                {
                    // this is not our protocol
                    continue;
                }
                
                Debug.WriteLine("NetConnection received protocol [{0} <- {1}]", LocalEndPoint, endPoint);

                if (receivedEncryption)
                {
                    try
                    {
                        await SendEncryptionAck();
                        sentEncryptionAck = true;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                        continue;
                    }
                }
                
                try
                {
                    await SendEncryption();
                    sentEncryption = true;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    continue;
                }
            }
            else
            {
                Header h;
                try
                {
                    h = new Header(packetBuffer.AsSpan(0, recvPktLen));
                    packetStream.Position += Header.HeaderSize;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    continue;
                }
                
                if (h.ProtocolId != _protocolId)
                {
                    continue;
                }

                switch (h.PacketType)
                {
                    case PacketType.Enk:
                    {
                        // received encryption
                        int keyLen = IPAddress.NetworkToHostOrder(packetReader.ReadInt32());
                        var keyBytes = packetReader.ReadBytes(keyLen);

                        if (!sentEncryption)
                        {
                            try
                            {
                                await SendEncryption();
                                sentEncryption = true;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e.Message);
                                continue;
                            }
                        }
                        
                        try
                        {
                            PublicKey otherPublicKey = PublicKey.Import(KeyAgreementAlgorithm.X25519, keyBytes,
                                KeyBlobFormat.RawPublicKey);
                            _secret = KeyAgreementAlgorithm.X25519.Agree(_key!, otherPublicKey);

                            if (_secret is null)
                            {
                                continue;
                            }

                            receivedEncryption = true;
                            Debug.WriteLine("NetConnection received encryption [{0} <- {1}]", LocalEndPoint, endPoint);
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine(e.Message);
                            continue;
                        }
                        
                        try
                        {
                            await SendEncryptionAck();
                            sentEncryptionAck = true;
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine(e.Message);
                            continue;
                        }
                    }
                    break;

                    case PacketType.EnkAck:
                    {
                        // other side has received our encryption
                        // connection established
                        receivedEncryptionAck = true;
                        Debug.WriteLine("NetConnection received ack encryption [{0} <- {1}]", LocalEndPoint, endPoint);
                        
                        if (receivedEncryption && sentEncryptionAck)
                        {
                            Debug.WriteLine("NetConnection connection establised [{0} <==> {1}]", LocalEndPoint, endPoint);
                            connectionEstablished = true;
                        }

                        if (!sentEncryption)
                        {
                            try
                            {
                                await SendEncryption();
                                sentEncryption = true;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e.Message);
                            }
                        }
                    }
                    break;
                }
            }
        }

        timer.Stop();
        
        if (connectionEstablished)
        {
            _remoteEp = endPoint;
            _connected = true;
            _socketThread.Start();
            _keepAliveTimer.Start();
        }
    }

    private int ReceivePacket(Memory<byte> dest)
    {
        int packetLen = 0;
        lock (_packets)
        {
            if (_packets.TryDequeue(out var packet))
            {
                packet.CopyTo(dest);
                packetLen = packet.Length;
            }

            if (_packets.Count == 0)
            {
                _packetsAvailableResetEvent.Reset();
            }
        }

        return packetLen;
    }

    public int Receive(Memory<byte> buffer)
    {
        _packetsAvailableResetEvent.Wait();

        lock (_packets)
        {
            int packetLen = 0;
            if (_packets.TryDequeue(out var packet))
            {
                packetLen = packet.Length;
                packet.CopyTo(buffer);
            }

            if (_packets.Count == 0)
            {
                _packetsAvailableResetEvent.Reset();
            }
            
            return packetLen;
        }
    }

    public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (_packetsAvailableResetEvent.IsSet)
        {
            return Task.FromResult(ReceivePacket(buffer));
        }
        else
        {
            var tcs = new TaskCompletionSource<int>();
            CancellationTokenRegistration ctr = token.Register(() =>
            {
                tcs.TrySetCanceled();
            });
            
            RegisteredWaitHandle h = ThreadPool.RegisterWaitForSingleObject(_packetsAvailableResetEvent.WaitHandle, (_, _) =>
            {
                tcs.TrySetResult(ReceivePacket(buffer));
            }, null, -1, executeOnlyOnce: true);

            _ = tcs.Task.ContinueWith(_ =>
            {
                h.Unregister(null);
                ctr.Unregister();
                
            }, CancellationToken.None);

            return tcs.Task;
        }
    }

    /// <exception cref="OverflowException"></exception>
    public async Task<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        if (!_connected || _remoteEp is null)
        {
            throw new SocketException();
        }
        
        using Key key = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(_secret!, ReadOnlySpan<byte>.Empty, new byte[12],
            AeadAlgorithm.Aes256Gcm);

        byte[] packetContent = AeadAlgorithm.Aes256Gcm.Encrypt(key, new byte[12], new byte[12], buffer.Span);
        if (packetContent.Length == 0)
        {
            return 0;
        }

        int packetLen = Header.HeaderSize + packetContent.Length;
        if (packetLen > _sendBuffer.Length - _sendPos)
        {
            throw new OverflowException();
        }

        bool send = false;
        int sentLen = 0;
        ArraySegment<byte> segment;

        lock (_sendBuffer)
        {
            var h = new Header
            {
                ProtocolId = _protocolId,
                PacketType = PacketType.Seq,
                PacketId = _sendWindow.NextPacketId
            };
            using var packetStream = new MemoryStream(_sendBuffer, _sendPos, packetLen);
            h.WriteBytes(packetStream);
            packetStream.Write(packetContent);
            _sendPos += packetLen;


            segment = new ArraySegment<byte>(_sendBuffer, _sendPos - packetLen, packetLen);

            if (!_sendWindow.IsBlocked)
            {
                _sendWindow.Push();

                _timer.Stop();
                _timer.Start();

                send = true;
            }
            
            var packet = new BufferedPacket(segment, h);
            _bufferedOutPackets[h.PacketId] = packet;
        }

        if (send)
        {
            sentLen = await _socket.SendToAsync(segment, SocketFlags.None, _remoteEp).ConfigureAwait(false);
        }

        return sentLen;
    }

    private int Send(Header h, ReadOnlyMemory<byte> buffer)
    {
        if (!_connected || _remoteEp is null)
        {
            throw new SocketException();
        }
        
        using Key key = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(_secret!, ReadOnlySpan<byte>.Empty, new byte[12],
            AeadAlgorithm.Aes256Gcm);

        byte[] packetContent = AeadAlgorithm.Aes256Gcm.Encrypt(key, new byte[12], new byte[12], buffer.Span);
        if (packetContent.Length == 0)
        {
            return 0;
        }

        int packetLen = Header.HeaderSize + packetContent.Length;
        if (packetLen > _sendBuffer.Length - _sendPos)
        {
            throw new OverflowException();
        }
        
        bool send = false;
        int sentLen = 0;
        ArraySegment<byte> segment;
        
        lock (_sendBuffer)
        {
            using var packetStream = new MemoryStream(_sendBuffer, _sendPos, packetLen);
            h.WriteBytes(packetStream);
            packetStream.Write(packetContent);
            _sendPos += packetLen;

            segment = new ArraySegment<byte>(_sendBuffer, _sendPos - packetLen, packetLen);

            if (!_sendWindow.IsBlocked)
            {
                _sendWindow.Push();

                _timer.Stop();
                _timer.Start();

                send = true;
            }
            
            var packet = new BufferedPacket(segment, h);
            _bufferedOutPackets[h.PacketId] = packet;
        }
        
        if (send)
        {
            sentLen = _socket.SendTo(segment, SocketFlags.None, _remoteEp);
        }

        return sentLen;
    }
    
    public int Send(ReadOnlyMemory<byte> buffer)
    {
        var h = new Header
        {
            ProtocolId = _protocolId,
            PacketType = PacketType.Seq,
            PacketId = _sendWindow.NextPacketId
        };
        return Send(h, buffer);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (_connected)
            {
                _connected = false;
                _socketThread.Join();
            }
            
            _timer.Dispose();
            _keepAliveTimer.Dispose();
            _socket.Dispose();
        }
    }
}