using Microsoft.Extensions.Logging.Abstractions;
using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.DeviceManager;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Infrastructure;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class AdbParserTests
{
    [Fact]
    public void Parses_devices_with_states_and_attributes()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            R58N12ABCDE            device usb:1-1 product:a51nsxx model:SM_A515F device:a51 transport_id:3
            9A1B2C3D               unauthorized usb:1-2 transport_id:4
            emulator-5554          offline
            ZY22ABC                recovery product:lmi model:Redmi_K30_Pro device:lmi transport_id:5

            """;
        var list = AdbParsers.ParseDevices(output);

        Assert.Equal(4, list.Count);
        Assert.Equal("R58N12ABCDE", list[0].Serial);
        Assert.Equal("device", list[0].State);
        Assert.Equal("SM A515F", list[0].Model);
        Assert.Equal("a51", list[0].DeviceCodename);
        Assert.Equal("unauthorized", list[1].State);
        Assert.Equal("offline", list[2].State);
        Assert.Equal("recovery", list[3].State);
    }

    [Theory]
    [InlineData("device", ConnectionMode.Adb)]
    [InlineData("unauthorized", ConnectionMode.AdbUnauthorized)]
    [InlineData("offline", ConnectionMode.AdbOffline)]
    [InlineData("recovery", ConnectionMode.Recovery)]
    [InlineData("sideload", ConnectionMode.Sideload)]
    [InlineData("no permissions", ConnectionMode.AdbOffline)]
    public void Maps_adb_states(string state, ConnectionMode mode) => Assert.Equal(mode, AdbDeviceDetector.MapState(state));

    [Fact]
    public void Parses_getprop()
    {
        const string output = "[ro.product.model]: [SM-A515F]\r\n[ro.build.version.release]: [13]\n[empty.value]: []\ngarbage line\n";
        var props = AdbParsers.ParseGetprop(output);
        Assert.Equal("SM-A515F", props["ro.product.model"]);
        Assert.Equal("13", props["ro.build.version.release"]);
        Assert.Equal("", props["empty.value"]);
        Assert.Equal(3, props.Count);
    }

    [Fact]
    public void Parses_packages_sorted()
    {
        var packages = AdbParsers.ParsePackages("package:com.whatsapp\npackage:com.android.chrome\r\nWARNING x\n");
        Assert.Equal(new[] { "com.android.chrome", "com.whatsapp" }, packages);
    }

    [Fact]
    public void Parses_device_policy_owners()
    {
        const string dumpsys = """
            Current Device Policy Manager state:
              Device Owner:
                admin=ComponentInfo{com.example.mdm/com.example.mdm.AdminReceiver}
                name=Example MDM
              Profile Owner (User 10):
                admin=ComponentInfo{com.work.app/.Admin}
            """;
        var admins = AdbParsers.ParseDeviceAdmins(dumpsys);
        Assert.Equal(2, admins.Count);
        Assert.Contains("Device Owner: com.example.mdm/com.example.mdm.AdminReceiver", admins);
        Assert.Contains("Profile Owner: com.work.app/.Admin", admins);
        Assert.Empty(AdbParsers.ParseDeviceAdmins("Current Device Policy Manager state:\n  Enabled Device Admins (User 0):\n"));
    }

    [Fact]
    public void Maps_props_to_device_without_guessing()
    {
        var entry = new AdbDeviceEntry("R58N", "device", new Dictionary<string, string>());
        var props = new Dictionary<string, string>
        {
            ["ro.product.manufacturer"] = "samsung",
            ["ro.product.model"] = "SM-A515F",
            ["ro.build.version.release"] = "13",
            ["ro.boot.flash.locked"] = "1",
            ["ro.boot.verifiedbootstate"] = "green",
            ["ro.board.platform"] = "exynos9611"
        };
        var d = AndroidDeviceMapper.FromAdb(entry, ConnectionMode.Adb, props);

        Assert.Equal("adb:R58N", d.Key);
        Assert.Equal("SM-A515F", d.Model);
        Assert.Equal("13", d.AndroidVersion);
        Assert.Equal("Locked (green)", d.BootloaderState);
        Assert.Contains("SamsungExynos", d.Chipset);
        Assert.Null(d.SecurityPatch); // not reported → stays empty
    }
}

public class FastbootParserTests
{
    [Fact]
    public void Parses_devices()
    {
        Assert.Equal(new[] { "abc123", "def456" }, FastbootParsers.ParseDevices("abc123\tfastboot\ndef456\t fastboot\n\n"));
        Assert.Empty(FastbootParsers.ParseDevices("< waiting for any device >"));
    }

    [Fact]
    public void Parses_getvar_all_from_stderr_format()
    {
        const string output = """
            (bootloader) product:lmi
            (bootloader) unlocked:no
            (bootloader) partition-type:system:raw
            (bootloader) partition-size:system: 0xC0000000
            (bootloader) current-slot: a
            all: Done!!
            Finished. Total time: 0.050s
            """;
        var vars = FastbootParsers.ParseGetvar(output);
        Assert.Equal("lmi", vars["product"]);
        Assert.Equal("no", vars["unlocked"]);
        Assert.Equal("raw", vars["partition-type:system"]);
        Assert.Equal("0xC0000000", vars["partition-size:system"]);
        Assert.Equal("a", vars["current-slot"]);
        Assert.False(vars.ContainsKey("all"));
    }

    [Fact]
    public void Maps_vars_to_device()
    {
        var d = FastbootDeviceDetector.FromVars("abc", new Dictionary<string, string> { ["product"] = "lmi", ["unlocked"] = "yes", ["is-userspace"] = "yes" });
        Assert.Equal(ConnectionMode.FastbootD, d.Mode);
        Assert.Equal("Unlocked", d.BootloaderState);
        Assert.Equal("lmi", d.DisplayName);
    }
}

public class CommandPolicyTests
{
    [Fact]
    public void Splits_with_quotes() =>
        Assert.Equal(new[] { "shell", "echo", "hello world" }, CommandLine.Split("shell echo \"hello world\""));

    [Theory]
    [InlineData("devices", CommandVerdict.Allowed)]
    [InlineData("adb shell getprop ro.product.model", CommandVerdict.Allowed)]
    [InlineData("shell pm list packages -3", CommandVerdict.Allowed)]
    [InlineData("logcat -d", CommandVerdict.Allowed)]
    [InlineData("shell settings put global adb_enabled 0", CommandVerdict.NeedsConfirmation)]
    [InlineData("shell rm /sdcard/a.txt", CommandVerdict.NeedsConfirmation)]
    [InlineData("reboot", CommandVerdict.NeedsConfirmation)]
    [InlineData("-s OTHERDEVICE shell getprop", CommandVerdict.Blocked)]
    [InlineData("connect 192.168.1.5", CommandVerdict.Blocked)]
    [InlineData("shell service call iphonesubinfo 1", CommandVerdict.Blocked)]
    [InlineData("shell dd if=/sdcard/x.img of=/dev/block/by-name/boot", CommandVerdict.Blocked)]
    [InlineData("shell", CommandVerdict.Blocked)]
    [InlineData("", CommandVerdict.Blocked)]
    public void Adb_policy(string command, CommandVerdict expected) =>
        Assert.Equal(expected, AdbCommandPolicy.Evaluate(command).Verdict);

    [Theory]
    [InlineData("getvar all", CommandVerdict.Allowed)]
    [InlineData("fastboot devices", CommandVerdict.Allowed)]
    [InlineData("oem device-info", CommandVerdict.Allowed)]
    [InlineData("reboot", CommandVerdict.NeedsConfirmation)]
    [InlineData("flash boot boot.img", CommandVerdict.Blocked)]
    [InlineData("erase userdata", CommandVerdict.Blocked)]
    [InlineData("flashing unlock", CommandVerdict.Blocked)]
    [InlineData("oem unlock", CommandVerdict.Blocked)]
    [InlineData("-w", CommandVerdict.Blocked)]
    [InlineData("-s other getvar all", CommandVerdict.Blocked)]
    public void Fastboot_policy(string command, CommandVerdict expected) =>
        Assert.Equal(expected, FastbootCommandPolicy.Evaluate(command).Verdict);
}

public class UsbDetectionTests
{
    [Fact]
    public void Reports_special_modes_and_driver_problems_only()
    {
        var entries = new[]
        {
            new PnpEntry(@"USB\VID_05C6&PID_9008\5&2A1B3C4D&0&2", "Qualcomm HS-USB QDLoader 9008 (COM7)", "Ports", 0),
            new PnpEntry(@"USB\VID_04E8&PID_685D\5&ABC&0&1", "SAMSUNG Mobile USB Modem", "Modem", 0),
            new PnpEntry(@"USB\VID_0E8D&PID_0003\5&XYZ&0&3", "MediaTek USB Port", null, 28),
            new PnpEntry(@"USB\VID_04E8&PID_6860\R58N12ABCDE", "SAMSUNG Android", "WPD", 0),
            new PnpEntry(@"USB\VID_18D1&PID_4EE0\abc", "Android Bootloader Interface", "AndroidUsbDeviceClass", 0), // fastboot → left to fastboot.exe
            new PnpEntry(@"USB\VID_046D&PID_C52B\5&1&0&4", "Logitech receiver", "HIDClass", 0)                      // not a phone vendor
        };

        var devices = UsbDeviceDetector.Build(entries);

        Assert.Equal(4, devices.Count);
        var edl = devices.Single(d => d.Mode == ConnectionMode.QualcommEdl);
        Assert.Equal("COM7", edl.ComPort);
        Assert.Equal(DriverState.Installed, edl.DriverState);
        Assert.Contains(devices, d => d.Mode == ConnectionMode.SamsungDownload);
        var mtk = devices.Single(d => d.Mode == ConnectionMode.MediaTekBrom);
        Assert.Equal(DriverState.Missing, mtk.DriverState);
        var mtp = devices.Single(d => d.Mode == ConnectionMode.Mtp);
        Assert.Equal("R58N12ABCDE", mtp.SerialNumber);
    }

    [Theory]
    [InlineData(@"USB\VID_04E8&PID_6860\R58N12ABCDE", "R58N12ABCDE")]
    [InlineData(@"USB\VID_05C6&PID_9008\5&2A1B3C4D&0&2", null)]
    public void Serial_from_instance_id(string id, string? expected) =>
        Assert.Equal(expected, UsbDeviceDetector.SerialFromInstanceId(id));

    [Fact]
    public void Same_serial_is_merged_different_serials_never()
    {
        var adb = new DeviceInfo { Key = "adb:R58N", Mode = ConnectionMode.Adb, Source = "ADB", SerialNumber = "R58N", Model = "SM-A515F" };
        var usbSame = new DeviceInfo { Key = "usb:1", Mode = ConnectionMode.Mtp, Source = "USB", SerialNumber = "r58n", UsbVendorId = 0x04E8, UsbProductId = 0x6860, DriverState = DriverState.Installed };
        var usbOther = new DeviceInfo { Key = "usb:2", Mode = ConnectionMode.QualcommEdl, Source = "USB", UsbVendorId = 0x05C6, UsbProductId = 0x9008 };

        var merged = DeviceManagerService.MergeSamePhysicalDevice([adb, usbSame, usbOther]);

        Assert.Equal(2, merged.Count);
        var phone = merged.Single(d => d.Key == "adb:R58N");
        Assert.Equal(ConnectionMode.Adb, phone.Mode);
        Assert.Equal((ushort)0x04E8, phone.UsbVendorId);
        Assert.Equal(DriverState.Installed, phone.DriverState);
        Assert.Contains(merged, d => d.Key == "usb:2");
    }
}

public class ChipsetTests
{
    [Theory]
    [InlineData("ro.board.platform", "mt6833", ChipsetFamily.MediaTek)]
    [InlineData("ro.board.platform", "kona", ChipsetFamily.Qualcomm)]
    [InlineData("ro.board.platform", "sm6150", ChipsetFamily.Qualcomm)]
    [InlineData("ro.board.platform", "exynos9611", ChipsetFamily.SamsungExynos)]
    [InlineData("ro.board.platform", "ums512", ChipsetFamily.Unisoc)]
    [InlineData("ro.soc.manufacturer", "QTI", ChipsetFamily.Qualcomm)]
    [InlineData("ro.board.platform", "unknownboard", ChipsetFamily.Unknown)]
    public void Detects_family(string key, string value, ChipsetFamily expected) =>
        Assert.Equal(expected, ChipsetDetector.FromProperties(new Dictionary<string, string> { [key] = value }));
}

public class ProcessRunnerTests
{
    [Fact]
    public async Task Runs_a_real_process_and_captures_output()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var result = await runner.RunAsync("dotnet", ["--version"], TimeSpan.FromSeconds(60));
        Assert.True(result.Success, result.Combined);
        Assert.Matches(@"^\d+\.\d+", result.StdOut);
    }

    [Fact]
    public async Task Missing_executable_throws()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        await Assert.ThrowsAnyAsync<Exception>(() => runner.RunAsync("definitely-not-a-tool-xyz", [], TimeSpan.FromSeconds(5)));
    }
}
