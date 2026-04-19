using System.Diagnostics;
using Spectre.Console;

bool plain = false;
bool interactive = false;
SortMode sortMode = SortMode.Interval;
int? logProblemNumber = null;
string? logDescription = null;
string? logLink = null;
long? logAnkiId = null;
bool importMode = false;
bool hideLastReview = false;
int? openProblemNumber = null;

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
            sortMode = SortMode.Due;
            break;
        case "last-review":
            sortMode = SortMode.LastReview;
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
        case "no-last-review":
            hideLastReview = true;
            break;
        case "open" or "o":
            if (i + 1 >= args.Length || !int.TryParse(args[++i], out int parsedOpenNum))
            {
                PrintError("--open requires a problem number");
                return 1;
            }
            openProblemNumber = parsedOpenNum;
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

if (openProblemNumber.HasValue)
{
    OpenProblem(openProblemNumber.Value);
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

        var toggleLabel = hideLastReview ? "Show Last Review column" : "Hide Last Review column";

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Log Review", "Open in Browser", "Refresh",
                    "Sort by Interval", "Sort by Due", "Sort by Last Review",
                    toggleLabel, "Exit"));

        if (choice == "Exit")
            break;
        if (choice == "Sort by Due")
            sortMode = SortMode.Due;
        else if (choice == "Sort by Interval")
            sortMode = SortMode.Interval;
        else if (choice == "Sort by Last Review")
            sortMode = SortMode.LastReview;
        else if (choice == toggleLabel)
            hideLastReview = !hideLastReview;
        else if (choice == "Log Review")
            DoInteractiveLog();
        else if (choice == "Open in Browser")
            DoInteractiveOpen();
    }
}

int? PickProblemInteractively(string title)
{
    var rawCards = repo.GetCardIntervals();
    var trackedByNumber = tracker.GetAllByProblemNumber();

    DateTimeOffset? LastReview(CardInterval c)
    {
        var parsed = ProblemParser.Parse(c.SortField);
        if (parsed is null) return null;
        return trackedByNumber.TryGetValue(parsed.Number, out var p) ? p.LastReviewed : null;
    }

    var currentCards = sortMode switch
    {
        SortMode.Due => rawCards.OrderBy(c => c.Due).ToList(),
        SortMode.LastReview => rawCards.OrderByDescending(LastReview).ToList(),
        _ => rawCards
    };

    var labelToNumber = new Dictionary<string, int>();
    foreach (var card in currentCards)
    {
        var parsed = ProblemParser.Parse(card.SortField);
        if (parsed is null) continue;
        var label = $"#{parsed.Number,-5} {parsed.Description}";
        labelToNumber.TryAdd(label, parsed.Number);
    }

    const string ManualChoice = "Enter problem number manually…";
    const string CancelChoice = "Cancel";

    var options = new List<string>(labelToNumber.Keys) { ManualChoice, CancelChoice };

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title(title)
            .PageSize(15)
            .AddChoices(options));

    if (choice == CancelChoice) return null;

    if (choice == ManualChoice)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<int>("LeetCode problem number:")
                .Validate(n => n > 0 ? ValidationResult.Success() : ValidationResult.Error("Must be positive")));
    }

    return labelToNumber[choice];
}

void DoInteractiveLog()
{
    var number = PickProblemInteractively("Which problem did you review?");
    if (number is null) return;

    tracker.LogReview(number.Value, null, null, null);
    var row = tracker.GetByProblemNumber(number.Value);
    var ts = row?.LastReviewed?.LocalDateTime.ToString("MM-dd-yy h:mm:ss tt");
    AnsiConsole.MarkupLine($"[green]Logged review for problem #{number.Value}[/] at [dim]{ts}[/]");
    AnsiConsole.MarkupLine("[dim]Press any key to continue…[/]");
    Console.ReadKey(intercept: true);
}

void DoInteractiveOpen()
{
    var number = PickProblemInteractively("Which problem do you want to open?");
    if (number is null) return;

    OpenProblem(number.Value);
    AnsiConsole.MarkupLine("[dim]Press any key to continue…[/]");
    Console.ReadKey(intercept: true);
}

void OpenProblem(int number)
{
    var row = tracker.GetByProblemNumber(number);
    if (row is null)
    {
        PrintError($"Problem #{number} is not in the tracker DB. Run --import first or use --log to add it.");
        return;
    }
    if (string.IsNullOrWhiteSpace(row.Link))
    {
        PrintError($"Problem #{number} has no link. Set one via --log {number} --link \"<url>\".");
        return;
    }

    if (plain)
        Console.WriteLine($"Opening {row.Link}");
    else
        AnsiConsole.MarkupLine($"[green]Opening[/] [dim]{Markup.Escape(row.Link)}[/]");

    try
    {
        Process.Start(new ProcessStartInfo(row.Link) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        PrintError($"Failed to launch browser: {ex.Message}");
    }
}

// ── Display ───────────────────────────────────────────────────────

void ShowSummary()
{
    var rawCards = repo.GetCardIntervals();

    if (rawCards.Count == 0)
    {
        PrintWarning("No cards found for the target deck.");
        return;
    }

    var trackedByNumber = tracker.GetAllByProblemNumber();

    DateTimeOffset? LastReviewTimestamp(CardInterval card)
    {
        var parsed = ProblemParser.Parse(card.SortField);
        if (parsed is null) return null;
        if (!trackedByNumber.TryGetValue(parsed.Number, out var problem)) return null;
        return problem.LastReviewed;
    }

    string LastReviewFor(CardInterval card)
    {
        var ts = LastReviewTimestamp(card);
        return ts.HasValue ? ts.Value.LocalDateTime.ToString("MM-dd-yy h:mm:ss tt") : "";
    }

    var cards = sortMode switch
    {
        SortMode.Due => rawCards.OrderBy(c => c.Due).ToList(),
        SortMode.LastReview => rawCards.OrderByDescending(LastReviewTimestamp).ToList(),
        _ => rawCards
    };

    var intervals = cards.Select(c => c.Interval).ToList();
    double mean = intervals.Average();
    double median = CalculateMedian(intervals);

    if (plain)
    {
        int dashWidth = hideLastReview ? 87 : 108;
        var header = $"{"Card",-60} {"Interval (days)",15} {"Due",10}";
        if (!hideLastReview) header += $" {"Last Review",20}";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', dashWidth));
        foreach (var card in cards)
        {
            var label = Truncate(card.SortField, 58);
            var line = $"{label,-60} {card.Interval,15} {card.Due.ToString("MM-dd-yy"),10}";
            if (!hideLastReview) line += $" {LastReviewFor(card),20}";
            Console.WriteLine(line);
        }
        Console.WriteLine(new string('-', dashWidth));
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
        if (!hideLastReview)
            table.AddColumn(new TableColumn("[bold]Last Review[/]").RightAligned());

        foreach (var card in cards)
        {
            var label = Markup.Escape(Truncate(card.SortField, 58));
            var ivlText = card.Interval switch
            {
                >= 365 => $"[green]{card.Interval}[/]",
                >= 21 => $"[blue]{card.Interval}[/]",
                _ => $"[yellow]{card.Interval}[/]"
            };
            if (hideLastReview)
            {
                table.AddRow(label, ivlText, card.Due.ToString("MM-dd-yy"));
            }
            else
            {
                var lastReview = LastReviewFor(card);
                var lastReviewText = lastReview.Length == 0 ? "[dim]—[/]" : lastReview;
                table.AddRow(label, ivlText, card.Due.ToString("MM-dd-yy"), lastReviewText);
            }
        }

        table.AddEmptyRow();
        if (hideLastReview)
        {
            table.AddRow("[dim]Cards[/]", $"[dim]{cards.Count}[/]", "");
            table.AddRow("[bold]Mean[/]", $"[bold]{mean:F1}[/]", "");
            table.AddRow("[bold]Median[/]", $"[bold]{median:F1}[/]", "");
        }
        else
        {
            table.AddRow("[dim]Cards[/]", $"[dim]{cards.Count}[/]", "", "");
            table.AddRow("[bold]Mean[/]", $"[bold]{mean:F1}[/]", "", "");
            table.AddRow("[bold]Median[/]", $"[bold]{median:F1}[/]", "", "");
        }

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
    Console.WriteLine("  -d, --due            Sort by Anki due date (default: sort by interval)");
    Console.WriteLine("  --last-review        Sort by our Last Review column (most recent first)");
    Console.WriteLine("  -l, --log <N>        Log a review for LeetCode problem #N");
    Console.WriteLine("      --desc <text>   Description for the logged problem");
    Console.WriteLine("      --link <url>    URL for the logged problem");
    Console.WriteLine("      --anki-id <id>  Anki card id to associate with the problem");
    Console.WriteLine("  --import             Sync problems from the Anki deck into the tracker DB");
    Console.WriteLine("  -o, --open <N>       Open LeetCode problem #N in the default browser");
    Console.WriteLine("  --no-last-review     Hide the Last Review column in the main display");
    Console.WriteLine("  --help, -h           Show this help");
}
