// See https://aka.ms/new-console-template for more information


using System.Net;
using System.Text;
using BeChat.Network;
using NSec.Cryptography;

// var bobKey = Key.Create(KeyAgreementAlgorithm.X25519);
// var aliceKey = Key.Create(KeyAgreementAlgorithm.X25519);
//
// var bobSecret = KeyAgreementAlgorithm.X25519.Agree(bobKey, aliceKey.PublicKey);
// var aliceSecret = KeyAgreementAlgorithm.X25519.Agree(aliceKey, bobKey.PublicKey);
//
// var bobEncKey = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(bobSecret, new byte[12], new byte[12], AeadAlgorithm.Aes256Gcm);
// var aliceEncKey = KeyDerivationAlgorithm.HkdfSha256.DeriveKey(aliceSecret, new byte[12], new byte[12], AeadAlgorithm.Aes256Gcm);
//
// var x = Encoding.ASCII.GetBytes( Convert.ToBase64String( Encoding.UTF8.GetBytes("Hello World") ) );
//
// var enc = AeadAlgorithm.Aes256Gcm.Encrypt(bobEncKey, new byte[12], new byte[12], x);
// var dec = AeadAlgorithm.Aes256Gcm.Decrypt(aliceEncKey, new byte[12], new byte[12], enc);
//
// var xx = Encoding.UTF8.GetString(Convert.FromBase64String( Encoding.ASCII.GetString(dec) ));
//
// Console.WriteLine("Done");


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

        Thread.Sleep(500);
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

