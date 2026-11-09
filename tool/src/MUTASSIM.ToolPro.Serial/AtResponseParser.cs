using System.Globalization;

namespace MUTASSIM.ToolPro.Serial;

/// <summary>
/// Pure parsing of AT responses per 3GPP TS 27.007 final result codes. No I/O here, so it is fully
/// unit-tested: the channel collects lines, this turns them into an <see cref="AtResponse"/>.
/// </summary>
public static class AtResponseParser
{
    private static readonly string[] PlainFinal =
        ["OK", "ERROR", "NO CARRIER", "BUSY", "NO DIALTONE", "NO ANSWER", "COMMAND NOT SUPPORT"];

    /// <summary>True when a line is a final result code that terminates an AT exchange.</summary>
    public static bool IsFinalResultCode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var t = line.Trim();
        if (PlainFinal.Contains(t, StringComparer.OrdinalIgnoreCase)) return true;
        return t.StartsWith("+CME ERROR:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("+CMS ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an <see cref="AtResponse"/> from the raw lines received for <paramref name="command"/>.
    /// If no final result code is present the exchange is treated as a timeout (or empty when nothing arrived).
    /// </summary>
    public static AtResponse Parse(string command, IReadOnlyList<string> rawLines, TimeSpan elapsed)
    {
        var lines = rawLines
            .Select(l => l.TrimEnd('\r', '\n'))
            .Where(l => l.Length > 0)
            .ToList();

        string? finalLine = null;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (IsFinalResultCode(lines[i])) { finalLine = lines[i].Trim(); break; }
        }

        if (finalLine is null)
            return new AtResponse(command, lines, lines.Count == 0 ? AtStatus.Empty : AtStatus.Timeout, null, null, elapsed);

        var (status, code) = Classify(finalLine);
        return new AtResponse(command, lines, status, finalLine, code, elapsed);
    }

    private static (AtStatus Status, int? Code) Classify(string finalLine)
    {
        if (finalLine.Equals("OK", StringComparison.OrdinalIgnoreCase))
            return (AtStatus.Ok, null);

        if (finalLine.StartsWith("+CME ERROR:", StringComparison.OrdinalIgnoreCase))
            return (AtStatus.CmeError, ParseCode(finalLine));

        if (finalLine.StartsWith("+CMS ERROR:", StringComparison.OrdinalIgnoreCase))
            return (AtStatus.CmsError, ParseCode(finalLine));

        // ERROR, NO CARRIER, BUSY, NO DIALTONE, NO ANSWER, COMMAND NOT SUPPORT → generic error.
        return (AtStatus.Error, null);
    }

    private static int? ParseCode(string finalLine)
    {
        var idx = finalLine.IndexOf(':');
        if (idx < 0 || idx + 1 >= finalLine.Length) return null;
        var rest = finalLine[(idx + 1)..].Trim();
        return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
