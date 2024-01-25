using System.Security.Cryptography;
using System.Text;
using BeChat.Client.Data;

namespace BeChat.Client.App;

public sealed class BeChatApp
{
    public BeChatApp()
    {
        
    }

    public static string GeneratePeerId(string phoneNumber)
    {
        using var sha = SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.ASCII.GetBytes(phoneNumber)));
    }
}