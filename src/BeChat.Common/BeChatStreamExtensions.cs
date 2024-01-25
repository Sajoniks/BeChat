using System.Net;

namespace BeChat.Common;

public static class BeChatStreamExtensions
{
    public static byte[] GetCompactEndpoint(this IPEndPoint endPoint)
    {
        var buffer = new byte[6];
        using var stream = new MemoryStream(buffer);
        using var writer = new BinaryWriter(stream);
        writer.WriteCompactEndpoint(endPoint);
        return buffer;
    }

    public static IPEndPoint ParseCompactEndpoint(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.Length != 6)
        {
            throw new NotSupportedException();
        }

        using var stream = new MemoryStream(buffer.ToArray());
        using var reader = new BinaryReader(stream);
        return ReadCompactEndpoint(reader);
    }

    public static IPEndPoint[] ParseCompactEndpoints(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.Length % 6 != 0)
        {
            throw new NotSupportedException();
        }

        var list = new List<IPEndPoint>();
        int num = buffer.Length / 6;

        using var stream = new MemoryStream(buffer.ToArray());
        using var reader = new BinaryReader(stream);
        
        while (num > 0)
        {
            list.Add(ReadCompactEndpoint(reader));
            --num;
        }

        return list.ToArray();
    }

    public static byte[] WriteCompactEndpoints(params IPEndPoint[] endPoints)
    {
        var buffer = new byte[endPoints.Length * 6];
        using var stream = new MemoryStream(buffer);
        using var writer = new BinaryWriter(stream);
        foreach (var ep in endPoints)
        {
            writer.WriteCompactEndpoint(ep);
        }

        return buffer;
    }
    
    public static int WriteCompactEndpoint(this BinaryWriter writer, IPEndPoint endPoint)
    {
        long prevPos = writer.BaseStream.Position;
        writer.Write( (uint) IPAddress.HostToNetworkOrder(BitConverter.ToInt32(endPoint.Address.GetAddressBytes())) );
        writer.Write( (ushort) IPAddress.HostToNetworkOrder((short) endPoint.Port) );
        long curPos = writer.BaseStream.Position;

        return (int)(curPos - prevPos);
    }

    public static IPEndPoint ReadCompactEndpoint(this BinaryReader reader)
    {
        var ip = (uint)IPAddress.NetworkToHostOrder(reader.ReadInt32());
        var port = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());

        return new IPEndPoint(ip, port);
    }
}