using System;
using System.Windows;
using System.Windows.Media;

namespace FastEthernetTransfer.Wpf.Styles;

public static class ThemeManager
{
    public static string CurrentTheme { get; private set; } = "Dark";

    public static event Action<string>? ThemeChanged;

    public static void ApplyTheme(string themeName)
    {
        bool isLight = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
        CurrentTheme = isLight ? "Light" : "Dark";

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        if (isLight)
        {
            SetColor(resources, "BgDarkColor", (Color)ColorConverter.ConvertFromString("#F1F5F9"));
            SetColor(resources, "CardBgColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetColor(resources, "CardBgHoverColor", (Color)ColorConverter.ConvertFromString("#F8FAFC"));
            SetColor(resources, "BorderColor", (Color)ColorConverter.ConvertFromString("#CBD5E1"));
            SetColor(resources, "BorderLightColor", (Color)ColorConverter.ConvertFromString("#E2E8F0"));
            SetColor(resources, "TextPrimaryColor", (Color)ColorConverter.ConvertFromString("#0F172A"));
            SetColor(resources, "TextSecondaryColor", (Color)ColorConverter.ConvertFromString("#475569"));
            SetColor(resources, "TextMutedColor", (Color)ColorConverter.ConvertFromString("#64748B"));

            SetBrush(resources, "BgDarkBrush", "#F1F5F9");
            SetBrush(resources, "CardBgBrush", "#FFFFFF");
            SetBrush(resources, "CardBgHoverBrush", "#F8FAFC");
            SetBrush(resources, "BorderBrush", "#CBD5E1");
            SetBrush(resources, "BorderLightBrush", "#E2E8F0");
            SetBrush(resources, "TextPrimaryBrush", "#0F172A");
            SetBrush(resources, "TextSecondaryBrush", "#475569");
            SetBrush(resources, "TextMutedBrush", "#64748B");
        }
        else
        {
            SetColor(resources, "BgDarkColor", (Color)ColorConverter.ConvertFromString("#0B0F19"));
            SetColor(resources, "CardBgColor", (Color)ColorConverter.ConvertFromString("#131B2A"));
            SetColor(resources, "CardBgHoverColor", (Color)ColorConverter.ConvertFromString("#1C263B"));
            SetColor(resources, "BorderColor", (Color)ColorConverter.ConvertFromString("#222F46"));
            SetColor(resources, "BorderLightColor", (Color)ColorConverter.ConvertFromString("#334155"));
            SetColor(resources, "TextPrimaryColor", (Color)ColorConverter.ConvertFromString("#F8FAFC"));
            SetColor(resources, "TextSecondaryColor", (Color)ColorConverter.ConvertFromString("#94A3B8"));
            SetColor(resources, "TextMutedColor", (Color)ColorConverter.ConvertFromString("#64748B"));

            SetBrush(resources, "BgDarkBrush", "#0B0F19");
            SetBrush(resources, "CardBgBrush", "#131B2A");
            SetBrush(resources, "CardBgHoverBrush", "#1C263B");
            SetBrush(resources, "BorderBrush", "#222F46");
            SetBrush(resources, "BorderLightBrush", "#334155");
            SetBrush(resources, "TextPrimaryBrush", "#F8FAFC");
            SetBrush(resources, "TextSecondaryBrush", "#94A3B8");
            SetBrush(resources, "TextMutedBrush", "#64748B");
        }

        ThemeChanged?.Invoke(CurrentTheme);
    }

    private static void SetColor(ResourceDictionary resources, string key, Color color)
    {
        resources[key] = color;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
        }
        else
        {
            var newBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            resources[key] = newBrush;
        }
    }
}
