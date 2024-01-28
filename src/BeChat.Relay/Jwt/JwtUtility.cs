using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using JsonConverter = Newtonsoft.Json.JsonConverter;
using JsonException = System.Text.Json.JsonException;

namespace BeChat.Relay.Jwt;

public class UserClaims
{
    public string UserName { get; init; } = "";
    public Guid UserId { get; init; } = Guid.Empty;
}

public class JwtIssuer
{
    private readonly IConfiguration _config;

    public JwtIssuer(IConfiguration configuration)
    {
        _config = configuration;
    }
    
    public interface IJwtExceptionHandler
    {
        public void OnExpired(TokenExpiredException e);
        public void OnTokenInvalid(TokenNotYetValidException e);
        public void OnVerificationFailed(SignatureVerificationException e);
        public void OnParseException(JsonException e);
    }

    public string CreateToken(string userName, Guid userId)
    {
        var token = JwtBuilder.Create()
            .WithAlgorithm(new HMACSHA256Algorithm())
            .AddClaim("UserName", userName)
            .AddClaim("UserId", userId)
            .AddClaim("exp", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
            .WithSecret(_config["Jwt:Secret"])
            .Encode();

        return token;
    }

    public UserClaims? Verify(string token, IJwtExceptionHandler? handler = null)
    {
        try
        {
            var claims = JwtBuilder.Create()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(_config["Jwt:Secret"])
                .MustVerifySignature()
                .Decode<UserClaims>(token);

            return claims;
        }
        catch (TokenExpiredException e)
        {
            handler?.OnExpired(e);
            return null;
        }
        catch (JsonException e)
        {
            handler?.OnParseException(e);
            return null;
        }
        catch (TokenNotYetValidException e)
        {
            handler?.OnTokenInvalid(e);
            return null;
        }
        catch (SignatureVerificationException e)
        {
            handler?.OnVerificationFailed(e);
            return null;
        }
    }
}