using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BeChat.Network;

/// <summary>
/// <c>NetConnectionFactory</c> is a helper that creates instances of <c>NetConnection</c> with valid Protocol ID
/// </summary>
public sealed class NetConnectionFactory
{
    private uint _protocolId;
    public static readonly NetConnectionFactory Default = new NetConnectionFactory();

    public NetConnectionFactory()
    {
        string appName = Assembly.GetEntryAssembly()?.FullName ?? "BeChat";
        using var sha = SHA256.Create();

        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(appName));
        _protocolId = BitConverter.ToUInt32(hash, 0) % 1000000;
    }

    public NetConnection Create()
    {
        return new NetConnection(_protocolId);
    }
}