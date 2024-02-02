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

    public Task<NetConnection> TraverseAsync(IPEndPoint localEp, params IPEndPoint[] endPoints)
    {
        if (endPoints.Length == 0)
        {
            throw new ArgumentException();
        }
        
        Task[] tasks = new Task[endPoints.Length];
        CancellationTokenSource cts = new CancellationTokenSource();
        NetConnection[] connections = new NetConnection[endPoints.Length];
        
        for (int i = 0; i < connections.Length; ++i)
        {
            connections[i] = Create();
            connections[i].Bind(localEp, true);
            int j = i;
            tasks[i] = Task.Factory.StartNew(() => 
                connections[j].ConnectAsync(endPoints[j], cts.Token)
            , CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        TaskCompletionSource<NetConnection> tcs = new();
        var t = Task.WhenAny(tasks);
        _ = t.ContinueWith(r =>
        {
            int task = Array.IndexOf(tasks, r.Result);
            NetConnection result = connections[task];

            try
            {
                cts.Cancel();
                Task.WaitAll(tasks);
                cts.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }

            for (int i = 0; i < connections.Length; i++)
            {
                if (i != task) connections[i].Dispose();
            }

            if (!result.Connected)
            {
                tcs.SetException(new SocketException());
            }
            else
            {
                tcs.SetResult(result);
            }
        }, CancellationToken.None);

        return tcs.Task;
    }

    public NetConnection Create()
    {
        return new NetConnection(_protocolId);
    }
}