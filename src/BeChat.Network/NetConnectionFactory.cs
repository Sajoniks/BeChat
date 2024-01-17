using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BeChat.Network;

/// <summary>
/// <c>NetConnectionFactory</c> is a helper that creates instances of <c>NetConnection</c> with valid Protocol ID
/// </summary>
public sealed class NetConnectionFactory
{
    private readonly uint _protocolId;
    public static readonly NetConnectionFactory Default = new(Assembly.GetEntryAssembly()?.GetName().Name ?? "BeChat");

    public NetConnectionFactory(string appName)
    {
        using var sha = SHA256.Create();

        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(appName));
        _protocolId = BitConverter.ToUInt32(hash, 0) % 1000000;
    }

    public NetConnection Traverse(IPEndPoint localEp, params IPEndPoint[] endPoints)
    {
        if (endPoints.Length == 0)
        {
            throw new ArgumentException();
        }
        
        Task[] tasks = new Task[endPoints.Length];
        using CancellationTokenSource cts = new CancellationTokenSource();
        NetConnection[] connections = new NetConnection[endPoints.Length];
        for (int i = 0; i < connections.Length; ++i)
        {
            connections[i] = Create();
            connections[i].Bind(localEp, true);
            int j = i;
            tasks[i] = Task.Factory.StartNew(() => connections[j].Connect(endPoints[j]), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        int task = Task.WaitAny(tasks);
        NetConnection result = connections[task];

        cts.Cancel();
        Task.WaitAll(tasks);
        
        for (int i = 0; i < connections.Length; i++)
        {
            if (i != task) connections[i].Dispose();
        }

        if (!result.Connected)
        {
            throw new SocketException();
        }

        return result;
    }
    
    public NetConnection Create()
    {
        return new NetConnection(_protocolId);
    }
}