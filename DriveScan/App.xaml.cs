using System.Windows;

namespace DriveScan;

public partial class App : Application
{
    public static string? StartScanPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse --scan "path" argument
        var args = e.Args;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scan" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                StartScanPath = args[i + 1].Trim('"');
                break;
            }
        }
    }
}
