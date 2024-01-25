using System.Text;
using BeChat.Bencode.Data;
using BeChat.Bencode.Serializer;

namespace BeChat.Common.Protocol;

public interface IBencodedPacket
{
    BDict BencodedSerialize();
    void BencodedDeserialize(BDict data);
}

public static class BeChatPacketSerializer
{
    public static void SerializePacket<T>(this T obj, Stream stream) where T : IBencodedPacket
    {
        var message = obj.BencodedSerialize();
        message.SerializeBytes(stream);
    }
    
    public static BDict SerializePacket<T>(this T obj) where T : IBencodedPacket
    {
        return obj.BencodedSerialize();
    }

    public static byte[] GetBytes<T>(this T obj) where T : IBencodedPacket
    {
        var message = obj.BencodedSerialize();
        return BencodeSerializer.SerializeBytes(obj);
    }

    public static T DeserializePacket<T>(Stream stream) where T : IBencodedPacket, new()
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var message = BencodeSerializer.Deserialize<BDict>(reader);
        var obj = new T();
        obj.BencodedDeserialize(message);
        return obj;
    }

    public static T FromBytes<T>(ReadOnlySpan<byte> buffer) where T : IBencodedPacket,  new()
    {
        var message = BencodeSerializer.Deserialize<BDict>(buffer);
        var inst = new T();
        inst.BencodedDeserialize(message);
        return inst;
    }
}