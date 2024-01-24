using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using NSec.Cryptography;

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
            if ((uint)b.Length != (uint)PacketHeaderFields.HeaderSize)
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
    private EndPoint? _remoteEp;

    private bool _connected = false;
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

    private bool _sendEncryption = false;
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

    public void Connect(IPEndPoint endPoint)
    {
        byte[] outgoingPacket = ArrayPool<byte>.Shared.Rent(128);
        byte[] incomingPacket = ArrayPool<byte>.Shared.Rent(128);
        BinaryPrimitives.WriteUInt32BigEndian(outgoingPacket, _protocolId);

        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        _socket.Blocking = false;

        bool connected = false;
        Stopwatch sendClock = new Stopwatch();

        try
        {
            _socket.SendTo(outgoingPacket, 4, SocketFlags.None, endPoint);
            sendClock.Start();

            while (!_connected)
            {
                try
                {
                    if (!connected && _socket.Poll(150, SelectMode.SelectRead))
                    {
                        int recv = _socket.ReceiveFrom(incomingPacket, ref ep);
                        var ipEp = ep as IPEndPoint ?? throw new InvalidProgramException();

                        if (ipEp.Port.Equals(endPoint.Port) && recv == 4 && !_sendEncryption)
                        {
                            uint recvProtocolId = BinaryPrimitives.ReadUInt32BigEndian(incomingPacket);
                            if (recvProtocolId == _protocolId)
                            {
                                _remoteEp = ep;
                                _key = new Key(KeyAgreementAlgorithm.X25519);

                                var header = new Header
                                {
                                    PacketType = PacketType.Enk,
                                    ProtocolId = _protocolId
                                };
                                
                                using var memoryStream = new MemoryStream();
                                header.WriteBytes(memoryStream);
                                var data = _key.Export(KeyBlobFormat.RawPublicKey);
                                memoryStream.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length)));
                                memoryStream.Write(data);

                                _socket.SendTo(memoryStream.ToArray(), _remoteEp);
                                _sendEncryption = true;
                            }
                        }
                        else if (_sendEncryption && _remoteEp is not null && _remoteEp.Equals(ipEp) && recv >= Header.HeaderSize)
                        {
                            var header = new Header(incomingPacket.AsSpan(0, Header.HeaderSize));
                            switch (header.PacketType)
                            {
                                case PacketType.Enk:
                                {
                                    using (var stream = new MemoryStream(incomingPacket, Header.HeaderSize, recv - Header.HeaderSize))
                                    using (var reader = new BinaryReader(stream))
                                    {
                                        int dataLen = IPAddress.NetworkToHostOrder(reader.ReadInt32());
                                        var data = reader.ReadBytes(dataLen);

                                        _secret = KeyAgreementAlgorithm.X25519.Agree(_key,
                                            PublicKey.Import(KeyAgreementAlgorithm.X25519, data,
                                                KeyBlobFormat.RawPublicKey));
                                    }
                                    
                                    // send ack
                                    using (var stream = new MemoryStream())
                                    {
                                        var ackHeader = new Header
                                        {
                                            ProtocolId = _protocolId,
                                            PacketType = PacketType.EnkAck
                                        };
                                        ackHeader.WriteBytes(stream);
                                        _socket.SendTo(stream.ToArray(), _remoteEp);
                                    }
                                }
                                break;

                                case PacketType.EnkAck:
                                {
                                    // we are ready 
                                    _connected = true;
                                }
                                break;
                            }
                        }
                    }
                }
                catch (SocketException)
                {
                    // ignored
                }

                
                if (sendClock.ElapsedMilliseconds >= 100)
                {
                    sendClock.Restart();
                    _socket.SendTo(outgoingPacket, 5, SocketFlags.None, endPoint);
                }
            }
        }
        catch (Exception)
        {
            // Failure
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(outgoingPacket);
            ArrayPool<byte>.Shared.Return(incomingPacket);
        }
    }

    public int Receive(byte[] buffer)
    {
        if (_remoteEp is null)
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