using System.Text;
using BeChat.Bencode.Data;

namespace BeChat.Bencode.Serializer;

public static class BencodedSerializerExtensions
{
    public static bool SerializeBytes(this BencodedBase bobject, Stream stream)
    {
        using var writer = new BinaryWriter(stream, encoding: Encoding.UTF8, leaveOpen: true);
        return BencodeSerializer.Serialize(writer, bobject);
    }

    public static byte[] SerializeBytes(this BencodedBase bobject)
    {
        using var stream = new MemoryStream();
        if (SerializeBytes(bobject, stream))
        {
            return stream.GetBuffer().AsSpan(0, (int)stream.Length).ToArray();
        }

        return Array.Empty<byte>();
    }
    
    public static string SerializeString(this BencodedBase bobject)
    {
        return Encoding.UTF8.GetString(SerializeBytes(bobject));
    }
}