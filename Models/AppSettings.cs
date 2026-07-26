namespace LumaClip.Models;

public enum AppTheme { System, Light, Dark }
public enum FloatingCompactStyle { Orb, Island }

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool StartWithWindows { get; set; }
    public bool LaunchMinimized { get; set; }
    public bool PauseCapture { get; set; }
    public bool PrivacyMode { get; set; }
    public bool HideSensitive { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool ReducedTransparency { get; set; }
    public int MaxHistoryItems { get; set; } = 20000;
    public int RetentionDays { get; set; } = 0;
    public int MaxImageMegabytes { get; set; } = 30;
    public string MainHotkey { get; set; } = "Ctrl+Shift+V";
    public string FloatHotkey { get; set; } = "Ctrl+Alt+V";
    public string[] ExcludedApps { get; set; } = ["1Password", "KeePass", "Bitwarden"];
    public string DataRoot { get; set; } = "";
    public bool FloatAlwaysOnTop { get; set; } = true;
    public bool FloatAutoCollapse { get; set; }
    public bool FloatClickThrough { get; set; }
    public double FloatOpacity { get; set; } = 0.94;
    public int FloatItemCount { get; set; } = 8;
    public double FloatLeft { get; set; } = double.NaN;
    public double FloatTop { get; set; } = double.NaN;
    public double FloatWidth { get; set; } = 390;
    public double FloatHeight { get; set; } = 560;
    public bool FloatCollapsed { get; set; }
    public FloatingCompactStyle FloatCompactStyle { get; set; } = FloatingCompactStyle.Orb;
}
