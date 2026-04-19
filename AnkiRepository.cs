using Microsoft.Data.Sqlite;

record CardInterval(long CardId, string SortField, int Interval, DateOnly Due);
record AnkiCard(long CardId, string SortField);

class AnkiRepository : IDisposable
{
    private readonly SqliteConnection _conn;

    // Deck: "Procedural Learning::FullLeetCode"
    private const long DeckId = 1772563462045;

    public AnkiRepository(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source=file:{dbPath}?immutable=1");
        _conn.Open();
    }

    public List<CardInterval> GetCardIntervals()
    {
        var epoch = GetCollectionEpoch();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, n.sfld, c.ivl, c.due
            FROM cards c
            JOIN notes n ON c.nid = n.id
            WHERE c.did = @deckId
              AND c.queue != -1
              AND c.type != 0
            ORDER BY c.ivl DESC
            """;
        cmd.Parameters.AddWithValue("@deckId", DeckId);

        var results = new List<CardInterval>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int dueDays = reader.GetInt32(3);
            var dueDate = epoch.AddDays(dueDays);

            results.Add(new CardInterval(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                dueDate
            ));
        }

        return results;
    }

    private DateOnly GetCollectionEpoch()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT crt FROM col LIMIT 1";
        var crt = (long)cmd.ExecuteScalar()!;
        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(crt).LocalDateTime);
    }

    public List<AnkiCard> GetAllDeckCards()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, n.sfld
            FROM cards c
            JOIN notes n ON c.nid = n.id
            WHERE c.did = @deckId
            """;
        cmd.Parameters.AddWithValue("@deckId", DeckId);

        var results = new List<AnkiCard>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new AnkiCard(reader.GetInt64(0), reader.GetString(1)));
        }
        return results;
    }

    public void Dispose() => _conn.Dispose();
}
