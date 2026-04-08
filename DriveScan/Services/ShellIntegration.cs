using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DriveScan.Services;

public static class ShellIntegration
{
    private const string KeyName = "disk0_ScanFolder";
    private const string RegPath = @"Directory\shell\" + KeyName;
    private const string RegBgPath = @"Directory\Background\shell\" + KeyName;

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(RegPath);
            return key != null;
        }
        catch { return false; }
    }

    public static bool Register()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            // Right-click on folder
            using (var key = Registry.ClassesRoot.CreateSubKey(RegPath))
            {
                key.SetValue("", "Scan with disk0");
                key.SetValue("Icon", exePath);
                using var cmd = key.CreateSubKey("command");
                cmd.SetValue("", $"\"{exePath}\" --scan \"%1\"");
            }

            // Right-click on folder background
            using (var key = Registry.ClassesRoot.CreateSubKey(RegBgPath))
            {
                key.SetValue("", "Scan with disk0");
                key.SetValue("Icon", exePath);
                using var cmd = key.CreateSubKey("command");
                cmd.SetValue("", $"\"{exePath}\" --scan \"%V\"");
            }

            return true;
        }
        catch { return false; }
    }

    public static bool Unregister()
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree(RegPath, false);
            Registry.ClassesRoot.DeleteSubKeyTree(RegBgPath, false);
            return true;
        }
        catch { return false; }
    }
}
