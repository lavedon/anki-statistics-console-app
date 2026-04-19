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

    private const int SchemaVersion = 2;

    private void Initialize()
    {
        long current = GetUserVersion();

        if (!TableExists("LeetcodeProblems"))
        {
            CreateSchema();
            SetUserVersion(SchemaVersion);
            return;
        }

        if (current < 1) MigrateV0toV1();
        if (current < 2) MigrateV1toV2();

        SetUserVersion(SchemaVersion);
    }

    private void CreateSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE LeetcodeProblems (
                ProblemId       INTEGER PRIMARY KEY AUTOINCREMENT,
                ProblemNumber   INTEGER NOT NULL,
                Description     TEXT,
                Link            TEXT,
                AnkiCardId      INTEGER UNIQUE,
                LastReviewed    TEXT,
                CreatedAt       TEXT NOT NULL
            );
            CREATE INDEX idx_leetcode_last_reviewed
                ON LeetcodeProblems(LastReviewed);
            CREATE INDEX idx_leetcode_problem_number
                ON LeetcodeProblems(ProblemNumber);
            """;
        cmd.ExecuteNonQuery();
    }

    private void MigrateV0toV1()
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

    private void MigrateV1toV2()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            BEGIN;
            CREATE TABLE LeetcodeProblems_new (
                ProblemId       INTEGER PRIMARY KEY AUTOINCREMENT,
                ProblemNumber   INTEGER NOT NULL,
                Description     TEXT,
                Link            TEXT,
                AnkiCardId      INTEGER UNIQUE,
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
            CREATE INDEX idx_leetcode_problem_number ON LeetcodeProblems(ProblemNumber);
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

    /// Log a review by AnkiCardId directly (unambiguous). Returns true if a row was updated.
    public bool LogReviewByCard(long ankiCardId, string? description = null, string? link = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE LeetcodeProblems SET
                LastReviewed = @now,
                Description  = COALESCE(@desc, Description),
                Link         = COALESCE(@link, Link)
            WHERE AnkiCardId = @anki;
            """;
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@anki", ankiCardId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// Log a review via problem number. Returns the variants list if >1 match (caller must disambiguate).
    /// Returns null on success (inserted or updated a single row).
    public List<LeetcodeProblem>? LogReview(int problemNumber, string? description, string? link, long? ankiCardId)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");

        if (ankiCardId.HasValue)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO LeetcodeProblems
                    (ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
                VALUES
                    (@num, @desc, @link, @anki, @now, @now)
                ON CONFLICT(AnkiCardId) DO UPDATE SET
                    LastReviewed  = @now,
                    ProblemNumber = @num,
                    Description   = COALESCE(@desc, Description),
                    Link          = COALESCE(@link, Link);
                """;
            cmd.Parameters.AddWithValue("@num", problemNumber);
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@anki", ankiCardId.Value);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
            return null;
        }

        var variants = GetAllByProblemNumberAsList(problemNumber);
        if (variants.Count > 1) return variants;

        if (variants.Count == 1)
        {
            var row = variants[0];
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                UPDATE LeetcodeProblems SET
                    LastReviewed = @now,
                    Description  = COALESCE(@desc, Description),
                    Link         = COALESCE(@link, Link)
                WHERE ProblemId = @pid;
                """;
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pid", row.ProblemId);
            cmd.ExecuteNonQuery();
            return null;
        }

        // Zero rows: insert a new orphan (no AnkiCardId yet)
        using var insertCmd = _conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO LeetcodeProblems
                (ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
            VALUES
                (@num, @desc, @link, NULL, @now, @now);
            """;
        insertCmd.Parameters.AddWithValue("@num", problemNumber);
        insertCmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@now", now);
        insertCmd.ExecuteNonQuery();
        return null;
    }

    public UpsertResult UpsertCatalog(int problemNumber, string? description, string? link, long ankiCardId)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");

        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText = "SELECT COUNT(*) FROM LeetcodeProblems WHERE AnkiCardId = @id";
        existsCmd.Parameters.AddWithValue("@id", ankiCardId);
        bool existed = (long)existsCmd.ExecuteScalar()! > 0;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LeetcodeProblems
                (ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt)
            VALUES
                (@num, @desc, @link, @anki, NULL, @now)
            ON CONFLICT(AnkiCardId) DO UPDATE SET
                ProblemNumber = @num,
                Description   = COALESCE(Description, @desc),
                Link          = COALESCE(Link, @link);
            """;
        cmd.Parameters.AddWithValue("@num", problemNumber);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@link", (object?)link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@anki", ankiCardId);
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
        var rows = GetAllByProblemNumberAsList(problemNumber);
        return rows.Count > 0 ? rows[0] : null;
    }

    public List<LeetcodeProblem> GetAllByProblemNumberAsList(int problemNumber)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems
            WHERE ProblemNumber = @num
            ORDER BY ProblemId
            """;
        cmd.Parameters.AddWithValue("@num", problemNumber);

        var results = new List<LeetcodeProblem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadProblem(reader));
        return results;
    }

    public LeetcodeProblem? GetByAnkiCardId(long ankiCardId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems
            WHERE AnkiCardId = @anki
            """;
        cmd.Parameters.AddWithValue("@anki", ankiCardId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProblem(reader) : null;
    }

    public Dictionary<long, LeetcodeProblem> GetAllByAnkiCardId()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ProblemId, ProblemNumber, Description, Link, AnkiCardId, LastReviewed, CreatedAt
            FROM LeetcodeProblems
            WHERE AnkiCardId IS NOT NULL
            """;

        var result = new Dictionary<long, LeetcodeProblem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var problem = ReadProblem(reader);
            if (problem.AnkiCardId.HasValue)
                result[problem.AnkiCardId.Value] = problem;
        }
        return result;
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
