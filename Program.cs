using Spectre.Console;

bool plain = false;
bool interactive = false;
bool sortByDue = false;
int? logProblemNumber = null;
string? logDescription = null;
string? logLink = null;
long? logAnkiId = null;
bool importMode = false;

for (int i = 0; i < args.Length; i++)
{
    var arg = args[i].TrimStart('-').ToLowerInvariant();
    switch (arg)
    {
        case "plain":
            plain = true;
            break;
        case "interactive":
            interactive = true;
            break;
        case "due" or "d":
            sortByDue = true;
            break;
        case "log" or "l":
            if (i + 1 >= args.Length || !int.TryParse(args[++i], out int parsedNum))
            {
                PrintError("--log requires a problem number");
                return 1;
            }
            logProblemNumber = parsedNum;
            break;
        case "desc":
            if (i + 1 >= args.Length)
            {
                PrintError("--desc requires a value");
                return 1;
            }
            logDescription = args[++i];
            break;
        case "link":
            if (i + 1 >= args.Length)
            {
                PrintError("--link requires a value");
                return 1;
            }
            logLink = args[++i];
            break;
        case "anki-id":
            if (i + 1 >= args.Length || !long.TryParse(args[++i], out long parsedAnki))
            {
                PrintError("--anki-id requires a card id");
                return 1;
            }
            logAnkiId = parsedAnki;
            break;
        case "import":
            importMode = true;
            break;
        case "help" or "h" or "?":
            PrintUsage();
            return 0;
        default:
            PrintError($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

var dbPath = @"C:\Users\laved\AppData\Roaming\Anki2\Luke\collection.anki2";
if (!File.Exists(dbPath))
{
    PrintError($"Database not found: {dbPath}");
    return 1;
}

using var repo = new AnkiRepository(dbPath);

var trackerPath = Path.Combine(AppContext.BaseDirectory, "Data", "leetcode.db");
using var tracker = new LeetcodeRepository(trackerPath);

if (logProblemNumber.HasValue)
{
    LogProblem(logProblemNumber.Value, logDescription, logLink, logAnkiId);
    return 0;
}

if (importMode)
{
    DoImport();
    return 0;
}

if (interactive)
{
    RunInteractive();
}
else
{
    ShowSummary();
}

return 0;

// ── Import action ─────────────────────────────────────────────────

void DoImport()
{
    var cards = repo.GetAllDeckCards();
    int inserted = 0, updated = 0, skipped = 0;
    var skippedCards = new List<AnkiCard>();

    if (!plain)
        AnsiConsole.MarkupLine("[bold]Importing from Anki deck…[/]");

    foreach (var card in cards)
    {
        var parsed = ProblemParser.Parse(card.SortField);
        if (parsed is null)
        {
            skipped++;
            skippedCards.Add(card);
            continue;
        }

        var result = tracker.UpsertCatalog(parsed.Number, parsed.Description, parsed.Link, card.CardId);
        if (result == UpsertResult.Inserted) inserted++; else updated++;

        if (plain)
        {
            var linkSuffix = parsed.Link is null ? "" : $"  {parsed.Link}";
            var tag = result == UpsertResult.Inserted ? "NEW" : "UPD";
            Console.WriteLine($"  [{tag}] #{parsed.Number,-5} {parsed.Description}{linkSuffix}");
        }
        else
        {
            var linkSuffix = parsed.Link is null ? "" : $" [dim]{Markup.Escape(parsed.Link)}[/]";
            var tag = result == UpsertResult.Inserted ? "[green]NEW[/]" : "[blue]UPD[/]";
            AnsiConsole.MarkupLine($"  {tag} [cyan]#{parsed.Number,-5}[/] {Markup.Escape(parsed.Description)}{linkSuffix}");
        }
    }

    foreach (var skip in skippedCards)
    {
        var preview = skip.SortField.Length > 80 ? skip.SortField[..80] + "…" : skip.SortField;
        if (plain)
            Console.WriteLine($"  [SKIP] card {skip.CardId}: {preview}");
        else
            AnsiConsole.MarkupLine($"  [yellow]SKIP[/] card {skip.CardId}: [dim]{Markup.Escape(preview)}[/]");
    }

    var summary = $"Imported {inserted} new, updated {updated}, skipped {skipped} (of {cards.Count} cards).";
    if (plain)
        Console.WriteLine($"\n{summary}");
    else
        AnsiConsole.MarkupLine($"\n[bold]{summary}[/]");
}

// ── Log action ────────────────────────────────────────────────────

void LogProblem(int number, string? desc, string? link, long? ankiId)
{
    tracker.LogReview(number, desc, link, ankiId);
    var row = tracker.GetByProblemNumber(number)!;
    var isoLocal = row.LastReviewed!.Value.LocalDateTime.ToString("MM-dd-yy HH:mm");

    if (plain)
    {
        Console.WriteLine($"Logged review for problem #{number} at {isoLocal}");
        if (!string.IsNullOrEmpty(row.Description))
            Console.WriteLine($"  Description: {row.Description}");
        if (!string.IsNullOrEmpty(row.Link))
            Console.WriteLine($"  Link:        {row.Link}");
        if (row.AnkiCardId.HasValue)
            Console.WriteLine($"  AnkiCardId:  {row.AnkiCardId}");
    }
    else
    {
        AnsiConsole.MarkupLine($"[green]Logged review for problem #{number}[/] at [dim]{isoLocal}[/]");
        if (!string.IsNullOrEmpty(row.Description))
            AnsiConsole.MarkupLine($"  [dim]Description:[/] {Markup.Escape(row.Description)}");
        if (!string.IsNullOrEmpty(row.Link))
            AnsiConsole.MarkupLine($"  [dim]Link:[/] {Markup.Escape(row.Link)}");
        if (row.AnkiCardId.HasValue)
            AnsiConsole.MarkupLine($"  [dim]AnkiCardId:[/] {row.AnkiCardId}");
    }
}

// ── Interactive mode ──────────────────────────────────────────────

void RunInteractive()
{
    while (true)
    {
        ShowSummary();
        Console.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Refresh", "Sort by Interval", "Sort by Due", "Exit"));

        if (choice == "Exit")
            break;
        if (choice == "Sort by Due")
            sortByDue = true;
        else if (choice == "Sort by Interval")
            sortByDue = false;
    }
}

// ── Display ───────────────────────────────────────────────────────

void ShowSummary()
{
    var cards = sortByDue
        ? repo.GetCardIntervals().OrderBy(c => c.Due).ToList()
        : repo.GetCardIntervals();

    if (cards.Count == 0)
    {
        PrintWarning("No cards found for the target deck.");
        return;
    }

    var intervals = cards.Select(c => c.Interval).ToList();
    double mean = intervals.Average();
    double median = CalculateMedian(intervals);

    if (plain)
    {
        Console.WriteLine($"{"Card",-60} {"Interval (days)",15} {"Due",10}");
        Console.WriteLine(new string('-', 87));
        foreach (var card in cards)
        {
            var label = Truncate(card.SortField, 58);
            Console.WriteLine($"{label,-60} {card.Interval,15} {card.Due.ToString("MM-dd-yy"),10}");
        }
        Console.WriteLine(new string('-', 87));
        Console.WriteLine($"{"Cards: " + cards.Count,-60}");
        Console.WriteLine($"{"Mean:",-60} {mean,15:F1}");
        Console.WriteLine($"{"Median:",-60} {median,15:F1}");
    }
    else
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.Title("[bold]Procedural Learning :: FullLeetCode[/]");

        table.AddColumn(new TableColumn("[bold]Card[/]").Width(60));
        table.AddColumn(new TableColumn("[bold]Interval (days)[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Due[/]").RightAligned());

        foreach (var card in cards)
        {
            var label = Markup.Escape(Truncate(card.SortField, 58));
            var ivlText = card.Interval switch
            {
                >= 365 => $"[green]{card.Interval}[/]",
                >= 21 => $"[blue]{card.Interval}[/]",
                _ => $"[yellow]{card.Interval}[/]"
            };
            table.AddRow(label, ivlText, $"{card.Due.ToString("MM-dd-yy")}");
        }

        table.AddEmptyRow();
        table.AddRow("[dim]Cards[/]", $"[dim]{cards.Count}[/]", "");
        table.AddRow("[bold]Mean[/]", $"[bold]{mean:F1}[/]", "");
        table.AddRow("[bold]Median[/]", $"[bold]{median:F1}[/]", "");

        AnsiConsole.Write(table);
    }
}

double CalculateMedian(List<int> values)
{
    var sorted = values.OrderBy(v => v).ToList();
    int n = sorted.Count;
    if (n == 0) return 0;
    if (n % 2 == 1) return sorted[n / 2];
    return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
}

string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..(max - 1)] + "…";

// ── Helpers ───────────────────────────────────────────────────────

void PrintError(string msg)
{
    if (plain)
        Console.Error.WriteLine($"ERROR: {msg}");
    else
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {Markup.Escape(msg)}");
}

void PrintWarning(string msg)
{
    if (plain)
        Console.WriteLine($"WARNING: {msg}");
    else
        AnsiConsole.MarkupLine($"[yellow]WARNING:[/] {Markup.Escape(msg)}");
}

void PrintUsage()
{
    Console.WriteLine("Usage: ankiStats [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --plain              Plain text output (no colors/tables)");
    Console.WriteLine("  --interactive        Interactive mode");
    Console.WriteLine("  -d, --due            Sort by due date (default: sort by interval)");
    Console.WriteLine("  -l, --log <N>        Log a review for LeetCode problem #N");
    Console.WriteLine("      --desc <text>   Description for the logged problem");
    Console.WriteLine("      --link <url>    URL for the logged problem");
    Console.WriteLine("      --anki-id <id>  Anki card id to associate with the problem");
    Console.WriteLine("  --import             Sync problems from the Anki deck into the tracker DB");
    Console.WriteLine("  --help, -h           Show this help");
}
