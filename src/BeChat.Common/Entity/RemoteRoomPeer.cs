namespace BeChat.Common.Entity;

public class RemoteRoomPeer : IRoomPeer
{
    public string UserName { get; }

    public RemoteRoomPeer(string userName)
    {
        UserName = userName;
    }
}