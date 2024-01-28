using BeChat.Common.Protocol;

namespace BeChat.Relay;

[AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RelayMessageHandlerAttribute : Attribute
{
    private readonly string _name;
    
    public RelayMessageHandlerAttribute(Type type)
    {
        _name = NetMessage.GetMessageId(type);
    }
    public string MessageName => _name;
}

public interface IRelayMessageNotify
{
    public void ReceiveRelayMessage(Response response);
}