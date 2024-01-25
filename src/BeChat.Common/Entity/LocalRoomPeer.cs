namespace BeChat.Common.Entity;

public class LocalRoomPeer : IRoomPeer
{
    public string UserName { get; }
    
    public LocalRoomPeer(string userName)
    {
        UserName = userName;
    }
}