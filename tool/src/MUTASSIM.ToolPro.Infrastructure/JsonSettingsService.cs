using System.Text.Json;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Configuration;

namespace MUTASSIM.ToolPro.Infrastructure;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _file;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonSettingsService(AppPaths paths, ILogger<JsonSettingsService> logger)
    {
        _file = paths.SettingsFile;
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DeviceScanIntervalSeconds = Math.Clamp(settings.DeviceScanIntervalSeconds, 1, 30);
        if (settings.Language is not ("ar" or "en")) settings.Language = "en";

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            File.Move(tmp, _file, overwrite: true); // atomic replace: no half-written settings
            Current = settings.Clone();
            _logger.LogInformation("Settings saved to {File}", _file);
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            // A corrupt file must not stop the program: keep a copy and fall back to defaults.
            _logger.LogWarning(ex, "Settings file unreadable, using defaults");
            try { File.Copy(_file, _file + ".corrupt", overwrite: true); } catch { /* best effort */ }
        }

        return new AppSettings();
    }
}
