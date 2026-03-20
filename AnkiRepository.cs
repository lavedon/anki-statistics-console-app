using Microsoft.Data.Sqlite;

record CardInterval(long CardId, string SortField, int Interval);

class AnkiRepository : IDisposable
{
    private readonly SqliteConnection _conn;

    // Deck: "Procedural Learning::FullLeetCode"
    private const long DeckId = 1772563462045;

    public AnkiRepository(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _conn.Open();
    }

    public List<CardInterval> GetCardIntervals()
    {
        using var tx = _conn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        using var cmd = _conn.CreateCommand();

        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT c.id, n.sfld, c.ivl
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
            results.Add(new CardInterval(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2)
            ));
        }

        tx.Commit();
        return results;
    }

    public void Dispose() => _conn.Dispose();
}
