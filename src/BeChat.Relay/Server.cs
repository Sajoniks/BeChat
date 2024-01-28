using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeChat.Relay;

public partial class Server
{
    private sealed class Result
    {
        public string? Error { get; }
        public bool IsOK { get; }
        
        private Queue<Response> _queue;

        private Result(bool isOk, string? errorMessage, IEnumerable<Response>? responses)
        {
            Error = errorMessage;
            IsOK = isOk;
            _queue = new Queue<Response>();
            if (responses is not null)
            {
                foreach (var response in responses)
                {
                    _queue.Enqueue(response);
                }
            }
        }
        
        public void AddResponse(Response response)
        {
            _queue.Enqueue(response);
        }

        public Response[] GetResponsesArray()
        {
            var arr = new Response[_queue.Count];
            _queue.CopyTo(arr, 0);
            return arr;
        }

        public static Result OK<T>(Request req, T message) where T : new()
        {
            var r = new Result(isOk: true, errorMessage: null, responses: null);
            r.AddResponse(req.CreateResponse(message));
            return r;
        }

        public static Result Exception(Request req, string message)
        {
            var r = new Result(isOk: false, errorMessage: message, responses: null);
            r.AddResponse(req.CreateError(message));
            return r;
        }

        public static Result FromResponse(Response response)
        {
            var r = new Result(isOk: !response.IsError,
                errorMessage: response.IsError ? response.ReadError().Message : null, responses: null);
            r.AddResponse(response);
            return r;
        }
    }
    
    private readonly IServiceProvider _provider;
    private readonly IConfiguration _configuration;

    public IServiceProvider Services => _provider;

    private delegate Task<Result> RpcDelegate(Request request, Socket socket);
    private readonly Dictionary<string, RpcDelegate> _rpcHandlers;

    private readonly ConcurrentDictionary<Socket, Guid> _userGuids = new();
    private HashSet<Guid> _connectedUsers = new();
    private readonly ConnectionFactory _connectionFactory;
    private readonly ConcurrentDictionary<Guid, Queue<Response>> _perUserEventQueue = new();


    public Server(IServiceCollection collection)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
     
        _rpcHandlers = new Dictionary<string, RpcDelegate>
        {
            { NetMessage<NetMessageLogin>.GetMessageId(), Login },
            { NetMessage<NetMessageAutoLogin>.GetMessageId(), AutoLogin },
            { NetMessage<NetMessageRegister>.GetMessageId(), Register },
            { NetMessage<NetMessageWelcome>.GetMessageId(), Welcome },
            { NetMessage<NetMessageFindContacts>.GetMessageId(), FindContacts },
            { NetMessage<NetMessageAddContact>.GetMessageId(), InviteToContact },
            { NetMessage<NetMessageAcceptContact>.GetMessageId(), AcceptInviteToContact }
        };
        
        _provider = collection.BuildServiceProvider();
        _configuration = _provider.GetRequiredService<IConfiguration>();
        _connectionFactory = new ConnectionFactory(_configuration["ConnectionStrings:Default"]!);
    }

    private Queue<Response> GetQueue(Guid userId)
    {
        return _perUserEventQueue.GetOrAdd(userId, (_) => new Queue<Response>());
    }

    private Queue<Response> GetQueue(Socket socket)
    {
        return _perUserEventQueue.GetOrAdd(_userGuids[socket], (_) => new Queue<Response>());
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

                    bool hasBuffer = client.Poll(1, SelectMode.SelectRead);
                    bool hasMessages = (_userGuids.ContainsKey(client) && _perUserEventQueue.GetOrAdd(_userGuids[client], (_) => new Queue<Response>()).Any());

                    if (hasBuffer || hasMessages)
                    {
                        if (hasBuffer && client.Receive(peekBuffer, SocketFlags.Peek) == 0)
                        {
                            _userGuids.TryRemove(client, out var guid);
                            _perUserEventQueue.Remove(guid, out _);

                            lock (_connectedUsers)
                            {
                                _connectedUsers.Remove(guid);
                            }
                            
                            Trace.TraceInformation($"Connection lost to {client.RemoteEndPoint}");
                            continue;
                        }
                        
                        ThreadPool.QueueUserWorkItem((x) =>
                        {
                            var curConn = x as TcpClient;
                            if (curConn is null)
                            {
                                return;
                            }
                            var poolClient = curConn.Client;

                            if (hasMessages)
                            {
                                Guid userId = Guid.Empty;
                                if (_userGuids.ContainsKey(poolClient))
                                {
                                    userId = _userGuids[poolClient];
                                }

                                if (userId != Guid.Empty)
                                {
                                    var messagesQueue = _perUserEventQueue[userId];
                                    while (messagesQueue.TryDequeue(out var response))
                                    {
                                        Trace.TraceInformation($"Send queued message to {poolClient.RemoteEndPoint}");
                                        poolClient.Send(response.GetBytes());
                                    }
                                }
                            }

                            if (!hasBuffer && hasMessages)
                            {
                                processedQueue.Enqueue(curConn);
                                return;
                            }
                            
                            var buffer = new byte[1024];

                            Request request;
                            try
                            {
                                int recv = poolClient.Receive(buffer);
                                request = Request.FromBytes(buffer.AsSpan(0, recv));
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError(
                                    $"Exception on client thread {Thread.CurrentThread.Name} while parsing request: {e.Message}");
                                return;
                            }

                            try
                            {
                                Trace.TraceInformation($"Request rpc {request.Name} from {poolClient.RemoteEndPoint}");

                                Result result = _rpcHandlers[request.Name].Invoke(request, poolClient).Result;

                                var responses = result.GetResponsesArray();
                                foreach (var r in responses)
                                {
                                    poolClient.Send(r.GetBytes());
                                }
                            }
                            catch (AggregateException e)
                            {
                                Trace.TraceError(
                                    $"Exception on client thread {Thread.CurrentThread.Name} while processing request: {e.Message}");
                            }
                            catch (Exception e)
                            {
                                Trace.TraceError(
                                    $"Exception on client thread {Thread.CurrentThread.Name} while processing request: {e.Message}");
                            }
                            
                            
                            Trace.TraceInformation($"Client processed {curConn.Client.RemoteEndPoint}");
                            processedQueue.Enqueue(curConn);
                            
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