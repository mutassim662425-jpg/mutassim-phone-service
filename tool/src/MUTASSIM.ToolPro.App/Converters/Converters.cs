using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using MUTASSIM.ToolPro.Core.Operations;
using MUTASSIM.ToolPro.Core.Platform;
using MUTASSIM.ToolPro.App.Workspace;
using MUTASSIM.ToolPro.Logging;

namespace MUTASSIM.ToolPro.App.Converters;

/// <summary>true → Visible. ConverterParameter "invert" reverses.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>null / empty string / 0 → Collapsed. ConverterParameter "invert" reverses.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int i => i == 0,
            System.Collections.ICollection c => c.Count == 0,
            _ => false
        };
        if (parameter as string == "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Any status enum → localized text (resource key "EnumType.Value").</summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionMode mode => mode.ToDisplayText(),
        Enum e => LocalizationService.ForEnum(e),
        null => "",
        _ => value.ToString() ?? ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Status enums / bool → semantic brush (success, warning, error, accent, muted).</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            bool b => b ? "Brush.Success" : "Brush.Error",
            ImplementationStatus s => s switch
            {
                ImplementationStatus.CompletedTested => "Brush.Success",
                ImplementationStatus.CompletedNeedsDeviceTest => "Brush.Accent",
                ImplementationStatus.RequiresExternalAuthorization => "Brush.Orange",
                ImplementationStatus.RequiresAdditionalProtocolOrHardware => "Brush.Warning",
                ImplementationStatus.NotSupported => "Brush.Error",
                _ => "Brush.TextMuted"
            },
            ServiceHealth h => h switch
            {
                ServiceHealth.Online => "Brush.Success",
                ServiceHealth.Offline => "Brush.Error",
                ServiceHealth.Checking => "Brush.Accent",
                _ => "Brush.TextMuted"
            },
            LicenseState l => l switch
            {
                LicenseState.Active => "Brush.Success",
                LicenseState.Expired or LicenseState.Invalid => "Brush.Error",
                LicenseState.NotActivated => "Brush.Orange",
                LicenseState.ServerUnreachable => "Brush.Warning",
                _ => "Brush.TextMuted"
            },
            UpdateState u => u switch
            {
                UpdateState.UpToDate => "Brush.Success",
                UpdateState.UpdateAvailable => "Brush.Orange",
                UpdateState.CheckFailed => "Brush.Error",
                UpdateState.Checking => "Brush.Accent",
                _ => "Brush.TextMuted"
            },
            OperationStatus o => o switch
            {
                OperationStatus.Succeeded => "Brush.Success",
                OperationStatus.Failed => "Brush.Error",
                OperationStatus.Cancelled => "Brush.Warning",
                _ => "Brush.Accent"
            },
            LogSeverity sev => sev switch
            {
                LogSeverity.Success => "Brush.Success",
                LogSeverity.Warning => "Brush.Warning",
                LogSeverity.Error => "Brush.Error",
                LogSeverity.Info => "Brush.Text",
                _ => "Brush.TextMuted"
            },
            DriverState d => d switch
            {
                DriverState.Installed => "Brush.Success",
                DriverState.Missing => "Brush.Error",
                DriverState.Problem => "Brush.Warning",
                _ => "Brush.TextMuted"
            },
            ConnectionMode m => m switch
            {
                ConnectionMode.Unknown => "Brush.TextMuted",
                ConnectionMode.AdbUnauthorized or ConnectionMode.AdbOffline => "Brush.Warning",
                ConnectionMode.QualcommEdl or ConnectionMode.MediaTekBrom or ConnectionMode.MediaTekPreloader
                    or ConnectionMode.SamsungDownload or ConnectionMode.AppleDfu => "Brush.Orange",
                _ => "Brush.Accent"
            },
            _ => "Brush.TextMuted"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Bytes → "1.25 GB".</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : "";

    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return string.Format(CultureInfo.InvariantCulture, unit == 0 ? "{0:0} {1}" : "{0:0.##} {1}", size, units[unit]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Operation-log line colour (GSM-tool style: values cyan, OK green, errors red).</summary>
public sealed class LogKindBrushConverter : IValueConverter
{
    private static readonly Brush ValueBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xC8, 0xF8)));
    private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE2)));
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x8C)));
    private static readonly Brush OkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0xC4, 0x6C)));
    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xB3, 0x3D)));
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x50, 0x5A)));
    private static readonly Brush HeaderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x89, 0x2A)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogKind.Value => ValueBrush,
        LogKind.Ok => OkBrush,
        LogKind.Warn => WarnBrush,
        LogKind.Error => ErrorBrush,
        LogKind.Muted => MutedBrush,
        LogKind.Header => HeaderBrush,
        _ => InfoBrush
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
