using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace LumaClip.Services;

public static class WindowsIntegration
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUND = 2;
    const int DWMSBT_MAINWINDOW = 2;
    const int DWMSBT_TRANSIENTWINDOW = 3;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_TOOLWINDOW = 0x80;

    public static bool IsSystemDark()
    {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        } catch { return false; }
    }

    public static bool IsReducedMotion()
    {
        try {
            return !SystemParameters.ClientAreaAnimation || !SystemParameters.MenuAnimation;
        } catch { return false; }
    }

    public static bool TryApplyBackdrop(Window window, bool dark, bool transient = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        try {
            var hwnd = new WindowInteropHelper(window).Handle;
            var darkValue = dark ? 1 : 0;
            var backdrop = transient ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
            var corner = DWMWCP_ROUND;
            var darkResult = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
            var backdropResult = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            return darkResult == 0 && backdropResult == 0;
        } catch { return false; }
    }

    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW : (style & ~WS_EX_TRANSPARENT) | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    public static (uint Modifiers, uint Key) ParseHotkey(string hotkey)
    {
        uint mods = 0, key = 0;
        foreach (var token in hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 0x0002;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 0x0001;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 0x0004;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= 0x0008;
            else if (token.Length == 1) key = char.ToUpperInvariant(token[0]);
            else if (token.StartsWith('F') && int.TryParse(token[1..], out var f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
        }
        return (mods, key);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr value);
    static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
}
