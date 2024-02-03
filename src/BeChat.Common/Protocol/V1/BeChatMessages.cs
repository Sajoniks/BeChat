namespace BeChat.Common.Protocol.V1;

[NetMessage]
public sealed class NetNotifyChatMessage
{
    [NetMessageProperty("usr_id")]
    public Guid UserId { get; init; } = Guid.Empty;

    [NetMessageProperty("m")]
    public string Content { get; init; } = "";
    
    [NetMessageProperty("dt")]
    public DateTime Timestamp { get; init; } = DateTime.MinValue;
}