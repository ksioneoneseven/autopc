using Microsoft.Data.Sqlite;

namespace AutoPilotAgent.Storage.Sqlite;

public sealed class SqliteRepository
{
    private readonly string _dbPath;

    public SqliteRepository(string dbPath)
    {
        _dbPath = dbPath;
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Plans(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CreatedUtc TEXT NOT NULL,
  Goal TEXT NOT NULL,
  PlanJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS LogEvents(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CreatedUtc TEXT NOT NULL,
  Level TEXT NOT NULL,
  Message TEXT NOT NULL
);
";
        cmd.ExecuteNonQuery();
    }

    public void InsertPlan(string goal, string planJson)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Plans(CreatedUtc, Goal, PlanJson) VALUES ($t,$g,$p);";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$g", goal);
        cmd.Parameters.AddWithValue("$p", planJson);
        cmd.ExecuteNonQuery();
    }

    public void InsertLog(string level, string message)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO LogEvents(CreatedUtc, Level, Message) VALUES ($t,$l,$m);";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$l", level);
        cmd.Parameters.AddWithValue("$m", message);
        cmd.ExecuteNonQuery();
    }
}
