using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using BeChat.Common;
using BeChat.Common.Entity;
using BeChat.Common.Protocol.V1.Messages;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using Newtonsoft.Json;

namespace BeChat.Relay;

public partial class Server
{
    private class Room
    {
        private readonly Guid _connectionGuid;
        private readonly Socket _hostSocket;
        private readonly IRoomPeer _peer;
        private readonly ConnectIpList _ipList;
        public Guid Guid => _connectionGuid;
        public IRoomPeer Peer => _peer;
        public Socket Socket => _hostSocket;
        public ConnectIpList IpList => _ipList;
        
        public Room(string roomName, IRoomPeer hoster, Socket hostSocket, ConnectIpList ipList)
        {
            _hostSocket = hostSocket;
            _peer = hoster;
            _ipList = ipList;
            
            if (roomName.Length == 0) throw new InvalidDataException("empty room name");
            using (var md5 = MD5.Create())
            {
                try
                {
                    _connectionGuid = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(roomName)));
                }
                catch (EncoderFallbackException e)
                {
                    throw new InvalidDataException("room name must be in UTF-8 encoding");
                }
            }

        }
    }

    private readonly ConcurrentDictionary<Guid, Room> _rooms;

    private Response Welcome(ClientRequest request, Socket client)
    {
        return Response.FromResponse( new WelcomeResponse("1.0.0") );
    }

    private Dictionary<string, string> _fakeDb = new();

    private Response Login(ClientRequest request, Socket client)
    {
        bool logged = false;
        
        var req = request.Cast<LoginRequest>();
        string userName = req.UserName;

        if (req.Token.Length > 0)
        {
            try
            {
                var claims = JwtBuilder.Create()
                    .WithAlgorithm(new HMACSHA256Algorithm())
                    .WithSecret("TEST_SECRET")
                    .MustVerifySignature()
                    .Decode<IDictionary<string, object>>(req.Token);
                
                logged = claims.ContainsKey("userName") && _fakeDb.ContainsKey(claims["userName"].ToString()!);
                if (logged)
                {
                    userName = claims["userName"].ToString()!;
                }
            }
            catch (TokenExpiredException e)
            {
                return ResponseFactory.FromError<LoginResponse>($"token expired by {DateTime.UtcNow - e.Expiration}",
                    0);
            }
            catch (TokenNotYetValidException e)
            {
                return ResponseFactory.FromError<LoginResponse>("token not yet valid",
                    0);
            }
            catch (SignatureVerificationException e)
            {
                return ResponseFactory.FromError<LoginResponse>($"token was not verified",
                    0);
            }
        }
        else if (_fakeDb.ContainsKey(req.UserName))
        {
            if (BCrypt.Net.BCrypt.Verify(req.Password, _fakeDb[req.UserName]))
            {
                logged = true;
            }
        }

        if (!logged)
        {
            return ResponseFactory.FromError<LoginResponse>("username or password does not match", 0);
        }
        else
        {
            var token = JwtBuilder.Create()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .AddClaim("exp", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
                .AddClaim("userName", userName)
                .WithSecret("TEST_SECRET")
                .Encode();
            
            return Response.FromResponse(new LoginResponse(token, userName));
        }
    }
    
    private Response Register(ClientRequest request, Socket client)
    {
        var req = request.Cast<RegisterRequest>();
        
        // @todo
        // work with db

        if (_fakeDb.ContainsKey(req.UserName))
        {
            return ResponseFactory.FromError<RegisterRequest>("user is already registered", 0);
        }
        else
        {
            string password = BCrypt.Net.BCrypt.HashPassword(req.Password);
            var token = JwtBuilder.Create()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .AddClaim("userName", req.UserName)
                .AddClaim("exp", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
                .WithSecret("TEST_SECRET")
                .Encode();

            _fakeDb.Add(req.UserName, password);
            
            return Response.FromResponse(new RegisterResponse(token, req.UserName));
        }
    }
    
    private Response Connect(ClientRequest request, Socket client)
    {
        return Response.FromResponse(new ConnectResponse());
    }
}