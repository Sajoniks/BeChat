using Npgsql;

namespace BeChat.Relay.Data;

public sealed class ConnectionFactory
{
    private readonly string _connString;
    
    public ConnectionFactory(string connectionString)
    {
        _connString = connectionString;
    }

    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_connString);
    }
}