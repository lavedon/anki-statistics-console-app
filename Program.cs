using Spectre.Console;

bool plain = false;
bool interactive = false;

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

if (interactive)
{
    RunInteractive();
}
else
{
    ShowSummary();
}

return 0;

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
                .AddChoices("Refresh", "Exit"));

        if (choice == "Exit")
            break;
    }
}

// ── Display ───────────────────────────────────────────────────────

void ShowSummary()
{
    var cards = repo.GetCardIntervals();

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
        Console.WriteLine($"{"Card",-60} {"Interval (days)",15}");
        Console.WriteLine(new string('-', 76));
        foreach (var card in cards)
        {
            var label = Truncate(card.SortField, 58);
            Console.WriteLine($"{label,-60} {card.Interval,15}");
        }
        Console.WriteLine(new string('-', 76));
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

        foreach (var card in cards)
        {
            var label = Markup.Escape(Truncate(card.SortField, 58));
            var ivlText = card.Interval switch
            {
                >= 365 => $"[green]{card.Interval}[/]",
                >= 21 => $"[blue]{card.Interval}[/]",
                _ => $"[yellow]{card.Interval}[/]"
            };
            table.AddRow(label, ivlText);
        }

        table.AddEmptyRow();
        table.AddRow("[dim]Cards[/]", $"[dim]{cards.Count}[/]");
        table.AddRow("[bold]Mean[/]", $"[bold]{mean:F1}[/]");
        table.AddRow("[bold]Median[/]", $"[bold]{median:F1}[/]");

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
    Console.WriteLine("  --plain         Plain text output (no colors/tables)");
    Console.WriteLine("  --interactive   Interactive mode");
    Console.WriteLine("  --help, -h      Show this help");
}
