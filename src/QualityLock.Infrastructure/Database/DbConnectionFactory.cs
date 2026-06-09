using MySqlConnector;

namespace QualityLock.Infrastructure.Database;

public interface IDbConnectionFactory
{
    MySqlConnection CreateConnection();
}

public class MySqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public MySqlConnection CreateConnection() => new(connectionString);
}
