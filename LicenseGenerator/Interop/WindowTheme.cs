using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LicenseGenerator.Interop;

/// <summary>Enables the immersive dark title bar on Windows 10 1809+ and Windows 11.</summary>
public static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    public static void ApplyDarkTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            SetDark(window);
        else
            window.SourceInitialized += (_, _) => SetDark(window);
    }

    private static void SetDark(Window window)
    {
        try
        {
            nint handle = new WindowInteropHelper(window).Handle;
            int enabled = 1;

            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Older Windows builds: keep the default title bar.
        }
        catch (EntryPointNotFoundException)
        {
            // Same as above.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
