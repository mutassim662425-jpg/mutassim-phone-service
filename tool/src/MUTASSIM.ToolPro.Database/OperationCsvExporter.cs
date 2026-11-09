using System.Globalization;
using System.Text;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;

namespace MUTASSIM.ToolPro.Database;

public static class OperationCsvExporter
{
    /// <summary>Writes UTF-8 (with BOM, so Excel shows Arabic correctly) RFC 4180 CSV.</summary>
    public static async Task ExportAsync(IEnumerable<OperationRecord> records, string filePath,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Date,Time,Manufacturer,Model,Mode,Service,Status,DurationSeconds,Result,Error");
        foreach (var r in records)
        {
            sb.AppendJoin(',',
                r.Id.ToString(CultureInfo.InvariantCulture),
                r.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.StartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Escape(r.Manufacturer),
                Escape(r.Model),
                Escape(r.Mode.ToDisplayText()),
                Escape(r.ServiceName),
                r.Status.ToString(),
                r.Duration?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
                Escape(r.ResultMessage),
                Escape(r.ErrorDetails));
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(true), cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Neutralise spreadsheet formula injection.
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
