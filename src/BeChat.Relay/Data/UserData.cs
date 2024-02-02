using System.Data;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay.Entites;
using Npgsql;

namespace BeChat.Relay.Data;

public sealed class UserData : IUserData
{
    private NpgsqlConnection _connection;
    
    public UserData(NpgsqlConnection connection)
    {
        _connection = connection;
        if (_connection.State == ConnectionState.Closed)
        {
            _connection.Open();
        }
    }

    public async Task<IEnumerable<NetMessageContact>> GetAllInvitationsAsync(Guid userId)
    {
        List<NetMessageContact> result = new();
        await using var cmd = new NpgsqlCommand(
            "SELECT r.from_id, u.username FROM friend_requests r LEFT JOIN users u ON r.from_id=u.user_id WHERE r.to_id=$1",
            _connection);

        cmd.Parameters.AddWithValue(userId);
        {
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync())
            {
                result.Add(new NetNotifyNewInvitation
                {
                    UserId = reader.GetGuid(0),
                    UserName = reader.GetString(1)
                });
            }
        }

        return result;
    }

    public async Task<IEnumerable<NetMessageContact>> GetAllFriendsAsync(Guid userId)
    {
        List<NetMessageContact> result = new();
        
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT f.user2_id, u.username, u.last_seen FROM friends f LEFT JOIN users u ON f.user2_id=u.user_id WHERE user1_id=$1", _connection);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(DateTime.UtcNow - TimeSpan.FromMinutes(1));

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(new NetMessageContact
                {
                    UserId = reader.GetGuid(0),
                    UserName = reader.GetString(1),
                    LastSeen = reader.GetDateTime(2),
                    IsOnline =  (reader.GetDateTime(2) >= DateTime.UtcNow - TimeSpan.FromMinutes(1))
                });
            }
        }
        
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT f.user1_id, u.username, u.last_seen FROM friends f LEFT JOIN users u ON f.user1_id=u.user_id WHERE user2_id=$1", _connection);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(DateTime.UtcNow - TimeSpan.FromMinutes(1));

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add(new NetMessageContact
                {
                    UserId = reader.GetGuid(0),
                    UserName = reader.GetString(1),
                    LastSeen = reader.GetDateTime(2),
                    IsOnline = (reader.GetDateTime(2) >= DateTime.UtcNow - TimeSpan.FromMinutes(1))
                });
            }
        }

        return result;
    }
    
    public async Task<UserDto?> FindUserByUserNameAsync(string userName)
    {
        await using var cmd = new NpgsqlCommand("SELECT user_id, password FROM users WHERE username=$1", _connection);
        cmd.Parameters.AddWithValue(userName);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            reader.Read();

            return new UserDto(
                Id: reader.GetGuid(0),
                userName: userName,
                password: reader.GetString(1)
            );
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<UserDto?> FindUserByUserIdAsync(Guid userId)
    {
        await using var cmd = new NpgsqlCommand("SELECT username, password FROM users WHERE user_id=$1", _connection);
        cmd.Parameters.AddWithValue(userId);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            reader.Read();

            return new UserDto(
                Id: userId,
                userName: reader.GetString(0),
                password: reader.GetString(1)
            );
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public async Task SendInvitationAsync(Guid fromUserId, Guid toUserId)
    {
        await using var cmd = new NpgsqlCommand("INSERT INTO friend_requests (request_id, from_id, to_id) VALUES($1, $2, $3)", _connection);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(fromUserId);
        cmd.Parameters.AddWithValue(toUserId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException e)
        {
            if (e.MessageText.StartsWith("(BECHAT-0001)"))
            {
                throw new FriendAlreadyAddedException();
            }
            else if (e.MessageText.StartsWith("(BECHAT-0002)"))
            {
                throw new InvitationAlreadySentException();
            }

            throw new InvitationAlreadySentException();
        }
        catch (Exception e)
        {
            throw new InvitationAlreadySentException();
        }
    }

    public async Task<DateTime> GetUserLastSeenAsync(Guid userId)
    {
        using var getLastSeenCmd = new NpgsqlCommand("SELECT last_seen FROM users WHERE user_ud=$1");
        getLastSeenCmd.Parameters.AddWithValue(userId);

        try
        {
            object? res = await getLastSeenCmd.ExecuteScalarAsync();
            if (res is null)
            {
                return DateTime.MinValue;
            }

            return (DateTime)res;
        }
        catch (Exception)
        {
            throw new UserDoesNotExistException();
        }
    }

    public async Task SetUserLastSeenAsync(Guid userId, DateTime time)
    {
        using var updateLastSeenCmd = new NpgsqlCommand("UPDATE users SET last_seen=$1 WHERE user_id=$2", _connection);
        updateLastSeenCmd.Parameters.AddWithValue(time);
        updateLastSeenCmd.Parameters.AddWithValue(userId);

        try
        {
            await updateLastSeenCmd.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // ignore
        }
    }
    
    public async Task<(NetMessageContact, NetMessageContact)> AcceptInvitationAsync(Guid fromUserId, Guid toUserId)
    {
        using var getUserNamesCmd = new NpgsqlCommand(
                "SELECT (SELECT username FROM users WHERE user_id=$1), (SELECT username FROM users WHERE user_id=$2)", _connection);

        getUserNamesCmd.Parameters.AddWithValue(fromUserId);
        getUserNamesCmd.Parameters.AddWithValue(toUserId);
        
        string fromUserName;
        string toUserName;
        try
        {
            await using var reader = await getUserNamesCmd.ExecuteReaderAsync().ConfigureAwait(false);
            await reader.ReadAsync().ConfigureAwait(false);

            fromUserName = reader.GetString(0);
            toUserName = reader.GetString(1);
        }
        catch (Exception)
        {
            throw new UserDoesNotExistException();
        }

        await using var tr = await _connection.BeginTransactionAsync().ConfigureAwait(false);
        using var deleteRequestCmd = new NpgsqlCommand("WITH del AS (DELETE FROM friend_requests WHERE (from_id=$1 AND to_id=$2) OR (from_id=$2 AND to_id=$1) RETURNING request_id) SELECT COUNT(*) FROM del", _connection, tr);
        deleteRequestCmd.Parameters.AddWithValue(fromUserId);
        deleteRequestCmd.Parameters.AddWithValue(toUserId);

        try
        {
            var deleted = (long?)(await deleteRequestCmd.ExecuteScalarAsync().ConfigureAwait(false)) ?? 0;
            if (deleted == 0)
            {
                throw new InvalidOperationException();
            }
        }
        catch (Exception)
        {
            await tr.RollbackAsync();
            throw new InvitationDoesNotExistException();
        }

        await using var addFriendCmd = new NpgsqlCommand("INSERT INTO friends VALUES ($1, $2, $3)", _connection, tr);
        addFriendCmd.Parameters.AddWithValue(Guid.NewGuid());
        addFriendCmd.Parameters.AddWithValue(fromUserId);
        addFriendCmd.Parameters.AddWithValue(toUserId);

        try
        {
            await addFriendCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            await tr.RollbackAsync().ConfigureAwait(false);
            throw new FriendAlreadyAddedException();
        }

        await tr.CommitAsync().ConfigureAwait(false);
        
        return (
                new NetMessageContact { UserId = fromUserId, UserName = fromUserName },
                new NetMessageContact { UserId = toUserId, UserName = toUserName }
            );
    }

    public async Task<IEnumerable<NetMessageContact>> QueryUsersByUserNameAsync(Guid userId, string query, int numResults)
    {
        List<NetMessageContact> result = new();
        
        await using var cmd = new NpgsqlCommand("SELECT user_id, username FROM users WHERE username LIKE $1 and user_id != $2 LIMIT $3", _connection);
        cmd.Parameters.AddWithValue(query+ "%");
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(numResults);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (reader.Read())
        {
            result.Add(new NetMessageContact
            {
                UserId = reader.GetGuid(0),
                UserName = reader.GetString(1)
            });
        }

        return result;
    }

    public async Task<Guid> CreateUserAsync(string userName, string password)
    {
        try
        {
            await using var cmd =
                new NpgsqlCommand("INSERT INTO users VALUES (gen_random_uuid(), $1, $2) RETURNING user_id",
                    _connection);
            cmd.Parameters.AddWithValue(userName);
            cmd.Parameters.AddWithValue(password);

            var guid = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return (Guid?)guid ?? Guid.Empty;
        }
        catch (Exception)
        {
            throw new UserAlreadyExistsException();
        }
    }
}