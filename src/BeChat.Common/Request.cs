using BeChat.Bencode.Data;
using BeChat.Bencode.Serializer;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;

namespace BeChat.Common;

public sealed class ClientRequest
{
    private readonly string _rpc;
    private readonly BDict _content;

    public string RpcName => _rpc;
    public BDict Content => _content;

    private ClientRequest(string rpc, BDict content)
    {
        _rpc = rpc;
        _content = content;
    }
    
    public static ClientRequest FromBytes(ReadOnlySpan<byte> buffer)
    {
        var request = BencodeSerializer.Deserialize<BDict>(buffer);
        var rpc = request["q"].ToString();

        var req = new ClientRequest(rpc, request);
        return req;
    }

    public T Cast<T>() where T : IBencodedPacket, new()
    {
        var inst = new T();
        inst.BencodedDeserialize(_content);
        return inst;
    }
}