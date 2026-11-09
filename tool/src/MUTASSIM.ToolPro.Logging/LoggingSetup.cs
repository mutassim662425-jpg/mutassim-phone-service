using Serilog;
using Serilog.Events;

namespace MUTASSIM.ToolPro.Logging;

public static class LoggingSetup
{
    /// <summary>Daily rolling file log (kept 30 days) + in-memory terminal sink.</summary>
    public static Serilog.Core.Logger CreateLogger(string logDirectory, InMemoryLogSink terminalSink)
    {
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "toolpro-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(terminalSink)
            .CreateLogger();
    }
}
