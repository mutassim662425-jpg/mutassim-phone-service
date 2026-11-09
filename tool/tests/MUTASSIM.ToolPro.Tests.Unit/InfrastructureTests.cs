using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Core.Configuration;
using MUTASSIM.ToolPro.Core.Security;
using MUTASSIM.ToolPro.Infrastructure;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class InfrastructureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mtp-infra-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Settings_roundtrip_and_validation()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        Assert.Equal("en", service.Current.Language);

        var s = service.Current.Clone();
        s.Language = "ar";
        s.DeviceScanIntervalSeconds = 999; // clamped
        s.UpdateFeedUrl = "https://updates.example.com/feed.json";
        await service.SaveAsync(s);

        var reloaded = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        Assert.Equal("ar", reloaded.Current.Language);
        Assert.Equal(30, reloaded.Current.DeviceScanIntervalSeconds);
        Assert.Equal("https://updates.example.com/feed.json", reloaded.Current.UpdateFeedUrl);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "{ this is not json");
        var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        Assert.Equal("en", service.Current.Language);
        Assert.True(File.Exists(paths.SettingsFile + ".corrupt"));
    }

    [Fact]
    public void Tool_locator_reports_missing_tool_honestly()
    {
        var settings = new StubSettings(new AppSettings { PlatformToolsPath = _root });
        Directory.CreateDirectory(_root);
        var status = new ExternalToolLocator(settings).Locate("definitely-not-a-real-tool-xyz");
        Assert.False(status.IsFound);
        Assert.Null(status.Path);
    }

    [Fact]
    public void Tool_locator_finds_tool_in_configured_folder()
    {
        Directory.CreateDirectory(_root);
        var name = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        File.WriteAllText(Path.Combine(_root, name), "");
        var status = new ExternalToolLocator(new StubSettings(new AppSettings { PlatformToolsPath = _root })).Locate("adb");
        Assert.True(status.IsFound);
        Assert.Equal(Path.Combine(_root, name), status.Path);
    }

    [Fact]
    public void Device_key_hash_is_stable_and_hides_serial()
    {
        var a = DeviceKeyHasher.Hash("adb:R58N12ABCDE");
        Assert.Equal(a, DeviceKeyHasher.Hash("adb:R58N12ABCDE"));
        Assert.NotEqual(a, DeviceKeyHasher.Hash("adb:R58N12ABCDF"));
        Assert.Equal(24, a.Length);
        Assert.DoesNotContain("R58N", a, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubSettings(AppSettings current) : ISettingsService
    {
        public AppSettings Current { get; } = current;
        public event EventHandler? Changed { add { } remove { } }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } // includes DirectoryNotFoundException
    }
}
