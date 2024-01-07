using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace BeChat.Network;

/// <summary>
///
/// <c>NetConnection</c> is a wrapper over UDP socket that provides a degree of reliability
/// 
/// </summary>
public sealed class NetConnection : IDisposable
{
    private readonly uint _protocolId;
    private readonly Socket _socket;
    private EndPoint? _remoteEp;

    public EndPoint? LocalEndPoint => _socket.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _remoteEp;
    public Socket Socket => _socket;
    
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
            PacketId = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(4));
            Ack = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8));
        }
        
        public uint ProtocolId { get; set; } = 0;
        public uint PacketId { get; set; } = 0;
        public uint Ack { get; set; } = 0;

        /// <summary>
        /// Writes structure to stream in Big Endian order
        /// </summary>
        public void WriteBytes(Stream s)
        {
            Span<byte> buffer = stackalloc byte[4];
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, ProtocolId);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, PacketId);
            s.Write(buffer);
            
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Ack);
            s.Write(buffer);
        }
    }

    private uint _packetsSent;
    private long _lastRecvPacketTime;
    private uint _mostRecentPacket;
    
    private bool _disposed;

    private enum PacketHeaderFields
    {
        ProtocolId = sizeof(uint),
        PacketNum = sizeof(uint),
        Ack = sizeof(uint),
        
        HeaderSize = ProtocolId + PacketNum + Ack
    }
    
    public NetConnection(uint protocolId)
    {
        _protocolId = protocolId;
        _disposed = false;
        _packetsSent = 0;
        _mostRecentPacket = 0;
        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
    }

    public void Bind(IPEndPoint endPoint)
    {
        _socket.Bind(endPoint);
    }

    public void Connect(IPEndPoint endPoint)
    {
        byte[] outgoingPacket = ArrayPool<byte>.Shared.Rent(5);
        byte[] incomingPacket = ArrayPool<byte>.Shared.Rent(5);
        BinaryPrimitives.WriteUInt32BigEndian(outgoingPacket, _protocolId);

        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        _socket.Blocking = false;

        bool connected = false;
        Stopwatch sendClock = new Stopwatch();
        Stopwatch recvClock = new Stopwatch();
        
        try
        {
            _socket.SendTo(outgoingPacket, 5, SocketFlags.None, endPoint);
            recvClock.Start();
            
            while (true)
            {
                if (!connected && _socket.Poll(100 * 1500, SelectMode.SelectRead))
                {
                    int recv = _socket.ReceiveFrom(incomingPacket, ref ep);
                    if (ep.Equals(endPoint) && recv == 5)
                    {
                        uint recvProtocolId = BinaryPrimitives.ReadUInt32BigEndian(incomingPacket);
                        if (recvProtocolId == _protocolId)
                        {
                            _remoteEp = ep;
                            connected = true;
                            recvClock.Start();
                            
                            // connection established
                            // keep sending packets for next n seconds to ensure that remote side will connect too
                        }
                    }
                }
                else if (connected && recvClock.ElapsedMilliseconds >= 1000)
                {
                    break;
                }

                if (sendClock.ElapsedMilliseconds >= 1500)
                {
                    sendClock.Restart();
                    _socket.SendTo(outgoingPacket, 5, SocketFlags.None, endPoint);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(outgoingPacket);
            ArrayPool<byte>.Shared.Return(incomingPacket);
        }
    }

    public int Receive(Span<byte> buffer)
    {
        if (_remoteEp is null)
        {
            throw new SocketException();
        }

        byte[] packetBuffer = Array.Empty<byte>();
        try
        {
            int headerSize = (int)PacketHeaderFields.HeaderSize;
            int packetLength = buffer.Length + headerSize;
            packetBuffer = ArrayPool<byte>.Shared.Rent(packetLength);
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            do
            {
                int recv = _socket.ReceiveFrom(packetBuffer, packetLength, SocketFlags.None, ref ep);
                if (recv >= headerSize)
                {
                    Header header = new Header(packetBuffer.AsSpan(0, headerSize));
                    if (header.ProtocolId == _protocolId)
                    {
                        _lastRecvPacketTime = (long) DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                        if (header.PacketId > _mostRecentPacket)
                        {
                            _mostRecentPacket = header.PacketId;
                        }

                        int contentLength = recv - headerSize;
                        packetBuffer.AsSpan(headerSize, contentLength).CopyTo(buffer);
                        return contentLength;
                    }
                }
                
            } while (true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }
    }

    public void Send(ReadOnlySpan<byte> buffer)
    {
        if (_remoteEp is null)
        {
            throw new SocketException();
        }

        byte[] packetBuffer = Array.Empty<byte>();
        try
        {
            int packetLength = buffer.Length + (int)PacketHeaderFields.HeaderSize;
            packetBuffer = ArrayPool<byte>.Shared.Rent(packetLength);
            
            Header header = new Header
            {
                ProtocolId = _protocolId,
                PacketId = _packetsSent,
                Ack = _mostRecentPacket,
            };

            using var stream = new MemoryStream(packetBuffer);
            
            header.WriteBytes(stream);
            stream.Write(buffer);
            
            _socket.SendTo(packetBuffer, packetLength, SocketFlags.None, _remoteEp);
            ++_packetsSent;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetBuffer);
        }
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