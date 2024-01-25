using BeChat.Bencode.Serializer;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;

namespace BeChat.Common;

public static class ResponseFactory
{
    public static Response FromError<T>(ResponseError error) where T : class, IBencodedPacket, new()
    {
        return Response.FromResponse( ResponseMessageFactory.FromError<T>(error) );
    }

    public static Response FromError<T>(Exception e, int errorCode) where T : class, IBencodedPacket, new()
    {
        return Response.FromResponse( ResponseMessageFactory.FromError<T>(e, errorCode) );
    }

    public static Response FromError<T>(string errorMessage, int errorCode) where T : class, IBencodedPacket, new()
    {
        return Response.FromResponse( ResponseMessageFactory.FromError<T>(errorMessage, errorCode) );
    }
}

public sealed class Response
{
    private readonly IBencodedPacket _responseContent;
    
    public static Response FromResponse<T>(ResponseMessage<T> message) where T : class, IBencodedPacket, new()
    {
        return new Response(message);
    }

    private Response(IBencodedPacket content)
    {
        _responseContent = content;
    }

    public byte[] GetBytes()
    {
        return BencodeSerializer.SerializeBytes(_responseContent);
    }
}