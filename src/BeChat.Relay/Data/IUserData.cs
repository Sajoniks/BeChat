using BeChat.Common.Protocol.V1;
using BeChat.Relay.Entites;

namespace BeChat.Relay.Data;

class UserAlreadyExistsException : Exception { }
class UserDoesNotExistException : Exception { }
class InvitationAlreadySentException : Exception { }
class InvitationDoesNotExistException : Exception { }
class FriendAlreadyAddedException : Exception { }

public interface IUserData
{
    public Task<IEnumerable<NetMessageContact>> GetAllInvitationsAsync(Guid userId);
    
    public Task<IEnumerable<NetMessageContact>> GetAllFriendsAsync(Guid userId);
    
    public Task<UserDto?> FindUserByUserNameAsync(string userName);
    
    public Task<UserDto?> FindUserByUserIdAsync(Guid userId);
    
    /// <exception cref="InvitationAlreadySentException"></exception>
    /// <exception cref="FriendAlreadyAddedException"></exception>
    /// <exception cref="UserDoesNotExistException"></exception>
    public Task SendInvitationAsync(Guid fromUserId, Guid toUserId);
    
    /// <exception cref="UserDoesNotExistException"></exception>
    /// <exception cref="InvitationDoesNotExistException"></exception>
    /// <exception cref="FriendAlreadyAddedException"></exception>
    public Task<(NetMessageContact, NetMessageContact)> AcceptInvitationAsync(Guid fromUserId, Guid toUserId);
    
    public Task<IEnumerable<NetMessageContact>> QueryUsersByUserNameAsync(Guid userId, string query, int numResults);
    
    /// <exception cref="UserAlreadyExistsException">This exception is thrown when user is already exists in the database</exception>
    public Task<Guid> CreateUserAsync(string userName, string password);
}