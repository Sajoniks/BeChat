using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BeChat.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeChat.Relay;

public partial class Server
{
    private readonly IServiceProvider _provider;
    private readonly IConfiguration _configuration;

    public IServiceProvider Services => _provider;

    private delegate Response RpcDelegate(ClientRequest request, Socket client);
    private readonly Dictionary<string, RpcDelegate> _rpcHandlers;

    
    public Server(IServiceCollection collection)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
     
        _rpcHandlers = new Dictionary<string, RpcDelegate>
        {
            { "connect", Connect },
            { "welcome", Welcome },
            { "login", Login },
            { "register", Register }
        };
        
        _provider = collection.BuildServiceProvider();
        _configuration = _provider.GetRequiredService<IConfiguration>();
        _rooms = new ConcurrentDictionary<Guid, Room>();
    }

    public void Run()
    {
        int port;
        try
        {
            port = Int32.Parse(_configuration["Host:Port"] ??
                               throw new NullReferenceException("Setting was not found: Host:Port"));
        }
        catch (Exception e)
        {
            Trace.TraceError($"Exception occured during initialization: {e.Message}");
            port = 0;
        }
        
        var cts = new CancellationTokenSource();
        var queue = new ConcurrentQueue<TcpClient>();
        var processedQueue = new ConcurrentQueue<TcpClient>();
        
        var listenerThread = new Thread(() =>
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            
            Trace.TraceInformation($"Hosting service on {listener.LocalEndpoint}");

            do
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                var conn = listener.AcceptTcpClient();
                Trace.TraceInformation($"Accepted connection from {conn.Client.RemoteEndPoint}");

                queue.Enqueue(conn);

            } while (true);
        });
        listenerThread.Name = "Listener Thread";
        
        try
        {
            var peekBuffer = new byte[1];
            listenerThread.Start();

            do
            {
                if (queue.TryDequeue(out var connection))
                {
                    var client = connection.Client;
                    
                    if (client.Poll(100 * 1000, SelectMode.SelectRead))
                    {
                        if (client.Receive(peekBuffer, SocketFlags.Peek) == 0)
                        {
                            Trace.TraceInformation($"Connection lost to {client.RemoteEndPoint}");
                            continue;
                        }
                        
                        Trace.TraceInformation($"Enqueued work client {client.RemoteEndPoint}");
                        
                        ThreadPool.QueueUserWorkItem((x) =>
                        {
                            var curConn = x as TcpClient;
                            if (curConn is null)
                            {
                                return;
                            }

                            var poolClient = curConn.Client;
                            var buffer = new byte[1024];
                            
                            ClientRequest request;
                            try
                            {
                                int recv = poolClient.Receive(buffer);
                                request = ClientRequest.FromBytes(buffer.AsSpan(0, recv));
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError(
                                    $"Exception on client thread {Thread.CurrentThread.Name} while parsing request: {e.Message}");
                                return;
                            }

                            try
                            {
                                Trace.TraceInformation($"Request rpc {request.RpcName} from {poolClient.RemoteEndPoint}");
                                
                                var response = _rpcHandlers[request.RpcName].Invoke(request, poolClient);
                                poolClient.Send(response.GetBytes());
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError(
                                    $"Exception on client thread {Thread.CurrentThread.Name} while processing request: {e.Message}");
                            }
                            
                            
                            Trace.TraceInformation($"Client processed {curConn.Client.RemoteEndPoint}");
                            processedQueue.Enqueue(curConn);

                            Trace.TraceInformation("Exit pool thread");

                        }, connection);
                    }
                    else
                    {
                        processedQueue.Enqueue(connection);
                    }
                }
                
                if (processedQueue.TryDequeue(out var processed))
                {
                    queue.Enqueue(processed);
                }

            } while (true);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception occured: {0}", e.Message);
        }
        finally
        {
            cts.Cancel();
            listenerThread.Join();
        }
    }
}