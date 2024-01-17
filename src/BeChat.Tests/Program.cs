// See https://aka.ms/new-console-template for more information


using System.Net;
using System.Text;
using BeChat.Network;

var factory = new NetConnectionFactory("BeChat.Test");

var ep1 = new IPEndPoint(IPAddress.Loopback, 50001);
var ep2 = new IPEndPoint(IPAddress.Loopback, 50002);

var t1 = new Thread(() =>
{
    var buffer = new byte[1024];

    string allowedChars = "123456789abcdefghijklmnopqrstuvwxyz";
    var sb = new StringBuilder();
    using var conn = factory.Traverse(ep1, ep2);
    while (true)
    {
        for (int j = 0; j < 5; ++j)
        {
            for (int i = 0; i < 20; ++i)
            {
                int idx = Random.Shared.Next(0, allowedChars.Length - 1);
                sb.Append(allowedChars[idx]);
            }

            var message = sb.ToString();
            sb.Clear();
            Console.WriteLine($"Thread {Thread.CurrentThread.Name} will send message: {message}");

            conn.Send(Encoding.UTF8.GetBytes(message));
        }

        int recv = conn.Receive(buffer);
        var recvMessage = Encoding.UTF8.GetString(buffer.AsSpan(0, recv));
        
        Console.WriteLine($"Thread {Thread.CurrentThread.Name} received message: {recvMessage}");

        Thread.Sleep(100);
    }
});
var t2 = new Thread(() =>
{
    var buffer = new byte[1024];
    
    string allowedChars = "123456789abcdefghijklmnopqrstuvwxyz";
    var sb = new StringBuilder();
    using var conn = factory.Traverse(ep2, ep1);
    while (true)
    {
        for (int i = 0; i < 20; ++i)
        {
            int idx = Random.Shared.Next(0, allowedChars.Length - 1);
            sb.Append(allowedChars[idx]);
        }

        var message = sb.ToString();
        sb.Clear();
        Console.WriteLine($"Thread {Thread.CurrentThread.Name} will send message: {message}");

        conn.Send(Encoding.UTF8.GetBytes(message));
        
        int recv = conn.Receive(buffer);
        var recvMessage = Encoding.UTF8.GetString(buffer.AsSpan(0, recv));
        
        Console.WriteLine($"Thread {Thread.CurrentThread.Name} received message: {recvMessage}");
        
        Thread.Sleep(1000);
    }
});

t1.Name = "Client Thread 1";
t1.Start();

t2.Name = "Client Thread 2";
t2.Start();

t1.Join();
t2.Join();

