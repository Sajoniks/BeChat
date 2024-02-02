using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using NSec.Cryptography;
using Timer = System.Timers.Timer;

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
        Ack
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
    private readonly System.Timers.Timer _ackTimer;
    private readonly Window _sendWindow;
    private readonly Window _recvWindow;

    private readonly Dictionary<uint, BufferedPacket> _bufferedInPackets = new();
    private readonly Dictionary<uint, BufferedPacket> _bufferdOutPackets = new();
    
    private readonly byte[] _recvBuffer = new byte[16 * 1024 * 1024];
    private int _recvPos = 0;
    private readonly byte[] _sendBuffer = new byte[16 * 1024 * 1024];
    private int _sendPos = 0;
    
    private bool _disposed = false;
    private volatile bool _timeout = false;

    private Key? _key;
    private SharedSecret? _secret;


    public NetConnection(uint protocolId)
    {
        _protocolId = protocolId;
        
        _sendWindow = new Window(Window.Mode.Sender, _windowSize);
        _recvWindow = new Window(Window.Mode.Receiver, _windowSize);
        
        _ackTimer = new System.Timers.Timer();
        _ackTimer.Elapsed += AckTimerTimeout;
        _ackTimer.Interval = 1000;
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    private void AckTimerTimeout(object? source, ElapsedEventArgs args)
    {
        if (_remoteEp is null)
        {
            throw new SocketException();
        }

        _timeout = true;
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
        
        Timer timer = new Timer();
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
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                continue;
            }

            if (!recvFromEp.Equals(endPoint))
            {
                continue;
            }
            
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
        }
    }

    public int Receive(byte[] buffer)
    {
        if (_remoteEp is null || !_connected)
        {
            throw new SocketException();
        }

        byte[] peek = new byte[1];
        while (true)
        {
            if (_timeout)
            {
                foreach (var outstandingPacketId in _sendWindow.OutstandingPackets)
                {
                    BufferedPacket outstandingPacket = _bufferdOutPackets[outstandingPacketId];
                    byte[] curPktData = outstandingPacket.Data.Array!;
                    _socket.SendTo(curPktData, outstandingPacket.Data.Offset, outstandingPacket.Data.Count, SocketFlags.None, _remoteEp);
            
                    // Console.WriteLine($"[{Thread.CurrentThread.Name}] Sent packet {outstandingPacketId} to {_remoteEp} because of timeout");
                }
                _timeout = false;
                _ackTimer.Stop();
                _ackTimer.Start();
            }
            
            if (!_socket.Poll(150, SelectMode.SelectRead))
            {
                continue;
            }

            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            int recvBytes = _socket.ReceiveFrom(_recvBuffer, _recvPos, _recvBuffer.Length - _recvPos, SocketFlags.None, ref ep);
            int totalRecvBytes = 0;
            
            if (recvBytes >= Header.HeaderSize && ep.Equals(_remoteEp))
            {
                var header = new Header(_recvBuffer.AsSpan(_recvPos, Header.HeaderSize));
                
                if (header.ProtocolId == _protocolId)
                {
                    int startRecvPos = _recvPos;
                    _recvPos += recvBytes;
                    
                    switch (header.PacketType)
                    {
                        case PacketType.Seq:
                        {
                            if (_recvWindow.IsInWindow(header.PacketId))
                            {
                                // Console.WriteLine($"[{Thread.CurrentThread.Name}] Received {header.PacketType} {header.PacketId} {_remoteEp} => {LocalEndPoint}");
                                
                                var segment = new ArraySegment<byte>(_recvBuffer, startRecvPos, recvBytes);
                                var bufferedPacket = new BufferedPacket(segment, header);
                                _bufferedInPackets[header.PacketId] = bufferedPacket;
                                
                                int available = _recvWindow.Put(header.PacketId);
                                if (available > 0)
                                {
                                    using var key = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(_secret, ReadOnlySpan<byte>.Empty, 
                                        new byte[12], AeadAlgorithm.Aes256Gcm);
                                    
                                    int writePos = 0;
                                    int realWritePos = 0;
                                    while (_recvWindow.AvailablePackets.TryDequeue(out uint bufferedPacketId))
                                    {
                                        BufferedPacket curPkt = _bufferedInPackets[bufferedPacketId];
                                        _bufferedInPackets.Remove(bufferedPacketId);
                                        
                                        var curPktData = curPkt.Data.Array!;
                                        var content = AeadAlgorithm.Aes256Gcm.Decrypt(key, new byte[12], new byte[12], curPktData.AsSpan(Header.HeaderSize, curPkt.Data.Count - Header.HeaderSize));

                                        Array.Copy(content, 0, buffer, writePos, content.Length);
                                        Array.Fill(_recvBuffer, Byte.MinValue, curPkt.Data.Offset, curPkt.Data.Count);

                                        writePos += content.Length;
                                        realWritePos += curPkt.Data.Count;
                                    }

                                    totalRecvBytes = writePos;
                                    _recvPos -= realWritePos;
                                }
                            }
                            
                            // send ack
                            {
                                var ackHeader = new Header
                                {
                                    ProtocolId = _protocolId,
                                    Ack = header.PacketId,
                                    PacketId = header.PacketId,
                                    PacketType = PacketType.Ack
                                };

                                using var ackMemoryStream = new MemoryStream();
                                ackHeader.WriteBytes(ackMemoryStream);

                                _socket.SendTo(ackMemoryStream.ToArray(), Header.HeaderSize, SocketFlags.None, _remoteEp);
                                
                                // Console.WriteLine($"[{Thread.CurrentThread.Name}] Sent {ackHeader.PacketType} {ackHeader.PacketId} {LocalEndPoint} => {_remoteEp}");
                            }
                        }
                        break;

                        case PacketType.Ack:
                        {
                            if (_sendWindow.IsInWindow(header.Ack))
                            {
                                // Console.WriteLine($"[{Thread.CurrentThread.Name}] Ack {header.Ack} accepted {_remoteEp} => {LocalEndPoint}");

                                int available = _sendWindow.Put(header.Ack);
                                if (available > 0)
                                {
                                    while (_sendWindow.AvailablePackets.TryDequeue(out uint packetId))
                                    {
                                        BufferedPacket curPkt = _bufferdOutPackets[packetId];
                                        _bufferdOutPackets.Remove(packetId);
                                        
                                        Array.Fill(_sendBuffer, Byte.MinValue, curPkt.Data.Offset, curPkt.Data.Count);
                                    }
                                }
                            }
                            else
                            {
                                // Console.WriteLine($"[{Thread.CurrentThread.Name}] Ack {header.Ack} ignored {_remoteEp} => {LocalEndPoint}");
                            }

                            if (_sendWindow.IsEmpty)
                            {
                                _ackTimer.Stop();
                            }
                        } 
                        break;
                    }
                }
                else
                {
                    Array.Fill(_recvBuffer, Byte.MinValue, _recvPos, recvBytes);
                }

                if (totalRecvBytes > 0)
                {
                    return totalRecvBytes;
                }
            }
        }
    }

    public int Send(ReadOnlySpan<byte> buffer)
    {
        if (_remoteEp is null)
        {
            throw new SocketException();
        }

        int packetTotalSize = Header.HeaderSize + buffer.Length;
        if (packetTotalSize > _sendBuffer.Length)
        {
            throw new OverflowException();
        }

        int send = 0;
        var header = new Header
        {
            ProtocolId = _protocolId,
            PacketType = PacketType.Seq,
            PacketId = _sendWindow.NextPacketId
        };

        using var memoryStream = new MemoryStream(_sendBuffer, _sendPos, _sendBuffer.Length - _sendPos);
        header.WriteBytes(memoryStream);

        using var key = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(_secret, ReadOnlySpan<byte>.Empty, new byte[12],
            AeadAlgorithm.Aes256Gcm);

        var content = AeadAlgorithm.Aes256Gcm.Encrypt(key, new byte[12], new byte[12], buffer);
        memoryStream.Write(content);
        
        
        int lastSendPos = _sendPos;
        int realPacketSize = content.Length + Header.HeaderSize;
        
        if (!_sendWindow.IsBlocked)
        {
            send = _socket.SendTo(_sendBuffer, _sendPos, realPacketSize, SocketFlags.None, _remoteEp);
            if (send > 0)
            {
                _sendWindow.Push();
                _sendPos += realPacketSize;
                
                // Console.WriteLine($"[{Thread.CurrentThread.Name}] Send {header.PacketType} {header.PacketId} {LocalEndPoint} => {_remoteEp}");

                // reset ack timer
                _ackTimer.Stop();
                _ackTimer.Start();
            }
            else
            {
                Array.Fill(_sendBuffer, Byte.MinValue, _sendPos, realPacketSize);
            }
        }

        if (send > 0)
        {
            // buffer it
            var segment = new ArraySegment<byte>(_sendBuffer, lastSendPos, realPacketSize);
            var bufferedPacket = new BufferedPacket(segment, header);
            _bufferdOutPackets[header.PacketId] = bufferedPacket;
        }

        return send;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _socket.Dispose();
            _disposed = true;
        }
    }
}