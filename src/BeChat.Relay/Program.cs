
using BeChat.Relay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<IConfiguration>(x =>
{
    var config = new ConfigurationBuilder()
        .AddJsonFile("serverProperties.json")
        .Build();

    return config;
});

var server = new Server(serviceCollection);
server.Run();