using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace MUTASSIM.ToolPro.App.Services;

/// <summary>
/// Runtime language switch (Arabic / English) with RTL support.
/// Strings live in Localization/Strings.{lang}.xaml and are consumed with {DynamicResource Key},
/// so XAML updates instantly; view models re-read texts on <see cref="LanguageChanged"/>.
/// </summary>
public sealed partial class LocalizationService(ILogger<LocalizationService> logger) : ObservableObject
{
    public static readonly IReadOnlyList<(string Code, string NativeName)> Supported =
        [("ar", "العربية"), ("en", "English")];

    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;

    public event EventHandler? LanguageChanged;

    public bool IsArabic => Language == "ar";

    public void Apply(string? language)
    {
        var lang = language == "ar" ? "ar" : "en";
        var app = Application.Current;
        if (app is null) return;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MutassimToolPro;component/Localization/Strings.{lang}.xaml",
                UriKind.Absolute)
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dictionary;
        else merged.Insert(0, dictionary);

        var culture = new CultureInfo(lang == "ar" ? "ar-LY" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        Language = lang;
        FlowDirection = lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        OnPropertyChanged(nameof(IsArabic));
        logger.LogInformation("Language set to {Language}", lang);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Localized string for a key; returns the key itself when missing (visible during development).</summary>
    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Localized text for any enum value, using the key "EnumType.Value".</summary>
    public static string ForEnum(Enum value)
    {
        var key = $"{value.GetType().Name}.{value}";
        var text = Application.Current?.TryFindResource(key) as string;
        return text ?? value.ToString();
    }
}
