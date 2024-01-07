using System.Globalization;
using System.Text;

namespace BeChat.Bencode.Serializer;

internal static class SerializeHelpers
{
    public static void ByteSerializeInt64(Stream stream, long item)
    {
        Span<byte> buffer = stackalloc byte[20];
        var bytesRead = Encoding.UTF8.GetBytes(item.ToString(CultureInfo.InvariantCulture).AsSpan(), buffer);
        stream.Write(buffer.Slice(0, bytesRead));
    }
}