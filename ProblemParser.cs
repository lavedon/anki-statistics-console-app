record ParsedSfld(int Number, string Description, string? Link);

static class ProblemParser
{
    public static ParsedSfld? Parse(string sfld)
    {
        if (string.IsNullOrWhiteSpace(sfld)) return null;

        // Skip leading "paste-<hash>.jpg" tokens and whitespace — some notes have
        // an image paste marker before the problem number.
        int pos = 0;
        while (pos < sfld.Length)
        {
            while (pos < sfld.Length && char.IsWhiteSpace(sfld[pos])) pos++;
            if (pos < sfld.Length && sfld.AsSpan(pos).StartsWith("paste-"))
            {
                while (pos < sfld.Length && !char.IsWhiteSpace(sfld[pos])) pos++;
                continue;
            }
            break;
        }

        string working = sfld[pos..];

        int dotIdx = working.IndexOf('.');
        if (dotIdx <= 0) return null;
        if (!int.TryParse(working.AsSpan(0, dotIdx), out int number)) return null;

        int restStart = dotIdx + 1;
        while (restStart < working.Length && working[restStart] == ' ')
            restStart++;

        string rest = working[restStart..];

        // Description lives on the first line.
        int nlIdx = rest.IndexOfAny(['\n', '\r']);
        if (nlIdx >= 0) rest = rest[..nlIdx];

        string? link = null;
        int urlStart = FindUrlStart(rest);
        string description;

        if (urlStart >= 0)
        {
            description = rest[..urlStart];
            int urlEnd = urlStart;
            while (urlEnd < rest.Length && !char.IsWhiteSpace(rest[urlEnd]) && rest[urlEnd] != '<')
                urlEnd++;
            link = rest[urlStart..urlEnd].TrimEnd(')', ']', '}', '.', ',', ';');
        }
        else
        {
            description = rest;
        }

        int pasteIdx = description.IndexOf("paste-", StringComparison.Ordinal);
        if (pasteIdx >= 0)
            description = description[..pasteIdx];

        description = description.Trim().TrimEnd('(', '[', '{', '-', ' ').Trim();
        if (description.Length == 0) return null;

        return new ParsedSfld(number, description, link);
    }

    private static int FindUrlStart(string s)
    {
        int https = s.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        int http = s.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (https >= 0 && http >= 0) return Math.Min(https, http);
        if (https >= 0) return https;
        return http;
    }
}
