using Microsoft.Data.Sqlite;

enum UpsertResult { Inserted, Updated }

record LeetcodeProblem(
    long ProblemId,
    int ProblemNumber,
    string? Description,
    string? Link,
    long? AnkiCardId,
    DateTimeOffset? LastReviewed,
    DateTimeOffset CreatedAt
);

class LeetcodeRepository : IDisposable
{
    private readonly SqliteConnection _conn;

    public LeetcodeRepository(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Initialize();
    }

    private const int SchemaVersion = 1;

    private void Initialize()
    {
        long current = GetUserVersion();
        if (current >= SchemaVersion) return;

        if (TableExists("LeetcodeProblems"))
            MigrateToV1();
        else
            CreateSchema();

        SetUserVersion(SchemaVersion);
    }

    private void CreateSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE LeetcodeProblems (
                ProblemId       INTEGER PRIMARY KEY AUTOINCREMENT,
                ProblemNumber   INTEGER NOT NULL UNIQUE,
                Description     TEXT,
                Link            TEXT,
                AnkiCardId      INTEGER,
                LastReviewed    TEXT,
                CreatedAt       TEXT NOT NULL
            );
            CREATE INDEX idx_leetcode_last_reviewed
                ON LeetcodeProblems(LastReviewed);
            """;
        cmd.ExecuteNonQuery();
    }

    private void MigrateToV1()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            BEGIN;
            CREATE TABLE LeetcodeProblems_new (
                ProblemId       INTEGER PRIMARY KEY AUTOINCREMENT,
                ProblemNumber   INTEGER NOT NULL UNIQUE,
                Description     TEXT,
                Link            TEXT,
                AnkiCardId      INTEGER,
                LastReviewed    TEXT,
                CreatedAt       TEXT NOT NULL
            );
            INSERT INTO LeetcodeProblems_new
                (ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
            SELECT
                ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems;
            DROP TABLE LeetcodeProblems;
            ALTER TABLE LeetcodeProblems_new RENAME TO LeetcodeProblems;
            CREATE INDEX idx_leetcode_last_reviewed ON LeetcodeProblems(LastReviewed);
            COMMIT;
            """;
        cmd.ExecuteNonQuery();
    }

    private bool TableExists(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private long GetUserVersion()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return (long)cmd.ExecuteScalar()!;
    }

    private void SetUserVersion(int version)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version}";
        cmd.ExecuteNonQuery();
    }

    public void LogReview(int problemNumber, string? description, string? link, long? ankiCardId)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LeetcodeProblems
                (ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
            VALUES
                (@num, @desc, @link, @anki, @now, @now)
            ON CONFLICT(ProblemNumber) DO UPDATE SET
                LastReviewed = @now,
                Description  = COALESCE(@desc, Description),
                Link         = COALESCE(@link, Link),
                AnkiCardId   = COALESCE(@anki, AnkiCardId);
            """;
        cmd.Parameters.AddWithValue("@num", problemNumber);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@anki", (object?)ankiCardId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    public UpsertResult UpsertCatalog(int problemNumber, string? description, string? link, long? ankiCardId)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");

        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText = "SELECT COUNT(*) FROM LeetcodeProblems WHERE ProblemNumber = @num";
        existsCmd.Parameters.AddWithValue("@num", problemNumber);
        bool existed = (long)existsCmd.ExecuteScalar()! > 0;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LeetcodeProblems
                (ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
            VALUES
                (@num, @desc, @link, @anki, NULL, @now)
            ON CONFLICT(ProblemNumber) DO UPDATE SET
                Description = COALESCE(Description, @desc),
                Link        = COALESCE(Link, @link),
                AnkiCardId  = COALESCE(@anki, AnkiCardId);
            """;
        cmd.Parameters.AddWithValue("@num", problemNumber);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@anki", (object?)ankiCardId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();

        return existed ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    public Dictionary<int, LeetcodeProblem> GetAllByProblemNumber()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems
            """;

        var result = new Dictionary<int, LeetcodeProblem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var problem = ReadProblem(reader);
            result[problem.ProblemNumber] = problem;
        }
        return result;
    }

    public LeetcodeProblem? GetByProblemNumber(int problemNumber)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems
            WHERE ProblemNumber = @num
            """;
        cmd.Parameters.AddWithValue("@num", problemNumber);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProblem(reader) : null;
    }

    private static LeetcodeProblem ReadProblem(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt32(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetInt64(4),
        reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
        DateTimeOffset.Parse(reader.GetString(6))
    );

    public void Dispose() => _conn.Dispose();
}
