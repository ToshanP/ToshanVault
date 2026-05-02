namespace ToshanVault.Data.Schema;

public interface IDbConnectionFactory
{
    Microsoft.Data.Sqlite.SqliteConnection Open();
}
