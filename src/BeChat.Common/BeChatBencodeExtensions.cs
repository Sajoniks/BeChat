using BeChat.Bencode.Data;

namespace BeChat.Common;

public static class BeChatBencodeExtensions
{
    public static Guid AsGuid(this BencodedBase bobject)
    {
        return new Guid(bobject.AsBytes().Span);
    }
}