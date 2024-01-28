using System.Net.Sockets;
using System.Text.Json;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay.Data;
using BeChat.Relay.Jwt;
using JWT.Exceptions;

namespace BeChat.Relay;

public partial class Server
{
    private Task<Result> Welcome(Request request, Socket socket)
    {
        return Task.FromResult( Result.OK(request, new NetMessageWelcome
        {
            Version = "1.0.0"
        }) );
    }

    private class JwtExceptionHandler : JwtIssuer.IJwtExceptionHandler
    {
        private Response? _error = null;
        private readonly Request _request;
        
        public JwtExceptionHandler(Request request)
        {
            _request = request;
        }

        public Response? Error => _error;
        
        public void OnExpired(TokenExpiredException e)
        {
            _error = _request.CreateError($"Token expired by {DateTime.UtcNow - e.Expiration}");
        }

        public void OnTokenInvalid(TokenNotYetValidException e)
        {
            _error = _request.CreateError("Invalid token");
        }

        public void OnVerificationFailed(SignatureVerificationException e)
        {
            _error = _request.CreateError("Invalid token");
        }

        public void OnParseException(JsonException e)
        {
            _error = _request.CreateError("Malformed token");
        }
    }
    
    private async Task<Result> AutoLogin(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageAutoLogin>();
        if (req.Token.Length == 0)
        {
            return Result.Exception( request, "Invalid token" );
        }

        var exHandler = new JwtExceptionHandler(request);
        var claims = new JwtIssuer(_configuration).Verify(req.Token, exHandler);

        if (exHandler.Error is not null)
        {
            return Result.FromResponse(exHandler.Error);
        }
        else
        {
            if (claims is null) throw new NullReferenceException();

            lock (_connectedUsers)
            {
                if (_connectedUsers.Any(x => x.Equals(claims.UserId)))
                {
                    return Result.Exception(request, "User is already connected");
                }
            }

            using var conn = _connectionFactory.CreateConnection();
            var service = new UserData(conn);
            var user = await service.FindUserByUserIdAsync(claims.UserId);
            if (user is null)
            {
                return Result.Exception(request, "User does not exist");
            }
                
            _userGuids.TryAdd(socket, claims.UserId);

            lock (_connectedUsers)
            {
                _connectedUsers.Add(claims.UserId);
            }
            
            {
                var queue = GetQueue(claims.UserId);
                var invitations = await service.GetAllInvitationsAsync(claims.UserId);
                var friends = await service.GetAllFriendsAsync(claims.UserId);

                foreach (var invitation in invitations)
                {
                    queue.Enqueue(Response.CreateGenericResponse(new NetNotifyNewInvitation()
                    {
                        UserId = invitation.UserId,
                        UserName = invitation.UserName
                    }));
                }

                foreach (var friend in friends)
                {
                    queue.Enqueue(Response.CreateGenericResponse(new NetNotifyNewFriend
                    {
                        UserId = friend.UserId,
                        UserName = friend.UserName
                    }));
                }
            }
                
            return Result.OK(request, new NetMessageUserData
            {
                Token = req.Token,
                UserName = claims.UserName
            });
        }
    }

    private async Task<Result> Login(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageLogin>();
        if (req.UserName.Length > 12 || req.UserName.Length < 3 || req.Password.Length > 12 || req.Password.Length < 3)
        {
            return Result.Exception(request, "Username or password is invalid");
        }
        
        using var conn = _connectionFactory.CreateConnection();
        var service = new UserData(conn);

        var user = await service.FindUserByUserNameAsync(req.UserName);
        if (user is null)
        {
            return Result.Exception(request, "User does not exist");
        }

        // check password
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.password))
        {
            return Result.Exception(request, "Username or password does not match");
        }

        lock (_connectedUsers)
        {
            if (_connectedUsers.Any(x => x.Equals(user.Id)))
            {
                return Result.Exception(request, "User is already connected");
            }
        }

        var token = new JwtIssuer(_configuration).CreateToken(req.UserName, user.Id);
        _userGuids.TryAdd(socket, user.Id);

        lock (_connectedUsers)
        {
            _connectedUsers.Add(user.Id);
        }

        {
            var queue = GetQueue(user.Id);
            var invitations = await service.GetAllInvitationsAsync(user.Id);
            var friends = await service.GetAllFriendsAsync(user.Id);

            foreach (var invitation in invitations)
            {
                queue.Enqueue(Response.CreateGenericResponse(invitation));
            }

            foreach (var friend in friends)
            {
                queue.Enqueue(Response.CreateGenericResponse(friend));
            }
        }

        return Result.OK(request, new NetMessageUserData
        {
            UserName = req.UserName,
            Token = token
        });
    }

    private async Task<Result> AcceptInviteToContact(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageAcceptContact>();
        
        var exHandler = new JwtExceptionHandler(request);
        var claims = new JwtIssuer(_configuration).Verify(req.Token, exHandler);

        if (exHandler.Error is not null)
        {
            return Result.FromResponse(exHandler.Error);
        }
        
        if (claims is null)
        {
            return Result.Exception(request, "Invalid user");
        }

        if (req.UserId == Guid.Empty)
        {
            return Result.Exception(request, "Invalid user");
        }

        using var conn = _connectionFactory.CreateConnection();
        var service = new UserData(conn);

        NetMessageContact c1;
        NetMessageContact c2;
        try
        {
            var users = await service.AcceptInvitationAsync(fromUserId: claims.UserId, toUserId: req.UserId);
            c1 = users.Item1;
            c2 = users.Item2;
        }
        catch (UserDoesNotExistException)
        {
            return Result.Exception(request, "Users does not exist");
        }
        catch (InvitationDoesNotExistException)
        {
            return Result.Exception(request, "Invitation has expired or does not exist");
        }
        catch (FriendAlreadyAddedException)
        {
            return Result.Exception(request, "User is already added to contact list");
        }

        {
            var queue = GetQueue(c1.UserId);
            queue.Enqueue(Response.CreateGenericResponse(new NetNotifyNewFriend
            {
                UserId = c2.UserId,
                UserName = c2.UserName
            }));
        }

        {
            var queue = GetQueue(c2.UserId);
            queue.Enqueue(Response.CreateGenericResponse(new NetNotifyNewFriend
            {
                UserId = c1.UserId,
                UserName = c1.UserName
            }));
        }

        return Result.OK(request, new NetMessageAck());
    }
    
    private async Task<Result> InviteToContact(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageAddContact>();
        
        var exHandler = new JwtExceptionHandler(request);
        var claims = new JwtIssuer(_configuration).Verify(req.Token, exHandler);

        if (exHandler.Error is not null)
        {
            return Result.FromResponse(exHandler.Error);
        }
        
        if (claims is null)
        {
            return Result.Exception(request, "Invalid user");
        }

        if (req.UserId == Guid.Empty)
        {
            return Result.Exception(request, "Invalid user");
        }

        using var conn = _connectionFactory.CreateConnection();
        var service = new UserData(conn);

        try
        {
            await service.SendInvitationAsync(fromUserId: claims.UserId, toUserId: req.UserId);
        }
        catch (InvitationAlreadySentException)
        {
            return Result.Exception(request, "Invitation to user has been sent already");
        }
        catch (FriendAlreadyAddedException)
        {
            return Result.Exception(request, "User is already in the contact list");
        }
        catch (UserDoesNotExistException)
        {
            return Result.Exception(request, "User does not exist");
        }

        {
            var queue = GetQueue(req.UserId);
            queue.Enqueue(Response.CreateGenericResponse(new NetNotifyNewInvitation
            {
                UserId = claims.UserId,
                UserName = claims.UserName
            }));
        }
        
        return Result.OK(request, new NetMessageAck());
    }

    private async Task<Result> FindContacts(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageFindContacts>();
        var exHandler = new JwtExceptionHandler(request);
        var claims = new JwtIssuer(_configuration).Verify(req.Token, exHandler);

        if (exHandler.Error is not null)
        {
            return Result.FromResponse(exHandler.Error);
        }

        if (claims is null)
        {
            return Result.Exception(request, "Invalid user");
        }

        if (req.QueryString.Length < 3 || req.QueryString.Length > 12)
        {
            return Result.Exception(request, "Query string is invalid");
        }

        using var conn = _connectionFactory.CreateConnection();
        var service = new UserData(conn);

        var contacts = await service.QueryUsersByUserNameAsync(claims.UserId, req.QueryString, 10);
        
        return Result.OK(request, new NetMessageFindContactsList
        {
            Contacts = contacts.ToList()
        });
    }
    
    private async Task<Result> Register(Request request, Socket socket)
    {
        var req = request.ReadContent<NetMessageRegister>();
        if ((req.UserName.Length < 3 || req.UserName.Length > 12) || req.Password.Length < 3)
        {
            return Result.Exception(request, "Invalid username or password");
        }

        using var conn = _connectionFactory.CreateConnection();
        var service = new UserData(conn);

        Guid newUserId;
        try
        {
            string password = BCrypt.Net.BCrypt.HashPassword(req.Password);
            newUserId = await service.CreateUserAsync(req.UserName, password);
        }
        catch (UserAlreadyExistsException)
        {
            return Result.Exception(request, "User is already registered");
        }
        
        var token = new JwtIssuer(_configuration).CreateToken(req.UserName, newUserId);

        _userGuids.TryAdd(socket, newUserId);

        lock (_connectedUsers)
        {
            _connectedUsers.Add(newUserId);
        }

        {
            var queue = GetQueue(newUserId);
            var invitations = await service.GetAllInvitationsAsync(newUserId);
            var friends = await service.GetAllFriendsAsync(newUserId);

            foreach (var invitation in invitations)
            {
                queue.Enqueue(Response.CreateGenericResponse(invitation));
            }

            foreach (var friend in friends)
            {
                queue.Enqueue(Response.CreateGenericResponse(friend));
            }
        }
        
        return Result.OK(request, new NetMessageUserData
        {
            Token = token,
            UserName = req.UserName
        });
    }
}