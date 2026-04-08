using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DriveScan.Models;

namespace DriveScan.Controls;

public enum ColorMode { Default, FileType, Age }

public abstract class ChartBase : FrameworkElement
{
    public static readonly DependencyProperty RootNodeProperty =
        DependencyProperty.Register(nameof(RootNode), typeof(DirectoryNode), typeof(ChartBase),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxDepthProperty =
        DependencyProperty.Register(nameof(MaxDepth), typeof(int), typeof(ChartBase),
            new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsRender));

    public DirectoryNode? RootNode
    {
        get => (DirectoryNode?)GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    public int MaxDepth
    {
        get => (int)GetValue(MaxDepthProperty);
        set => SetValue(MaxDepthProperty, value);
    }

    public DirectoryNode? ScanRoot { get; set; }
    public DirectoryNode? HoveredNode { get; protected set; }
    public DirectoryNode? SelectedNode { get; set; }
    public ColorMode ColorMode { get; set; } = ColorMode.Default;

    public long DriveTotalBytes { get; set; }
    public long DriveFreeBytes { get; set; }

    public event Action<DirectoryNode?>? HoverChanged;
    public event Action<DirectoryNode>? NodeClicked;
    public event Action<DirectoryNode>? ZoomRequested;
    public event Action<DirectoryNode>? DeleteRequested;

    protected Point? _mousePosition;

    private readonly DispatcherTimer _hoverThrottle;
    private bool _hoverDirty;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(46, 139, 87),
        Color.FromRgb(30, 100, 200),
        Color.FromRgb(200, 50, 50),
        Color.FromRgb(180, 140, 30),
        Color.FromRgb(140, 70, 160),
        Color.FromRgb(220, 120, 30),
        Color.FromRgb(60, 180, 120),
        Color.FromRgb(170, 80, 80),
        Color.FromRgb(80, 150, 180),
        Color.FromRgb(120, 160, 40),
        Color.FromRgb(200, 80, 160),
        Color.FromRgb(100, 120, 200),
    ];

    // File type color mapping
    private static readonly Dictionary<string, Color> FileTypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".mp4"] = Color.FromRgb(220, 50, 50), [".mkv"] = Color.FromRgb(220, 50, 50),
        [".avi"] = Color.FromRgb(220, 50, 50), [".mov"] = Color.FromRgb(220, 50, 50),
        [".wmv"] = Color.FromRgb(220, 50, 50), [".flv"] = Color.FromRgb(220, 50, 50),
        // Images
        [".jpg"] = Color.FromRgb(50, 180, 80), [".jpeg"] = Color.FromRgb(50, 180, 80),
        [".png"] = Color.FromRgb(50, 180, 80), [".gif"] = Color.FromRgb(50, 180, 80),
        [".bmp"] = Color.FromRgb(50, 180, 80), [".svg"] = Color.FromRgb(50, 180, 80),
        [".tif"] = Color.FromRgb(50, 180, 80), [".tiff"] = Color.FromRgb(50, 180, 80),
        [".webp"] = Color.FromRgb(50, 180, 80), [".raw"] = Color.FromRgb(50, 180, 80),
        // Audio
        [".mp3"] = Color.FromRgb(220, 160, 30), [".wav"] = Color.FromRgb(220, 160, 30),
        [".flac"] = Color.FromRgb(220, 160, 30), [".aac"] = Color.FromRgb(220, 160, 30),
        [".ogg"] = Color.FromRgb(220, 160, 30), [".wma"] = Color.FromRgb(220, 160, 30),
        // Documents
        [".pdf"] = Color.FromRgb(50, 120, 200), [".doc"] = Color.FromRgb(50, 120, 200),
        [".docx"] = Color.FromRgb(50, 120, 200), [".xls"] = Color.FromRgb(50, 120, 200),
        [".xlsx"] = Color.FromRgb(50, 120, 200), [".ppt"] = Color.FromRgb(50, 120, 200),
        [".pptx"] = Color.FromRgb(50, 120, 200), [".txt"] = Color.FromRgb(50, 120, 200),
        // Archives
        [".zip"] = Color.FromRgb(180, 80, 180), [".rar"] = Color.FromRgb(180, 80, 180),
        [".7z"] = Color.FromRgb(180, 80, 180), [".tar"] = Color.FromRgb(180, 80, 180),
        [".gz"] = Color.FromRgb(180, 80, 180),
        // Executables
        [".exe"] = Color.FromRgb(200, 100, 40), [".msi"] = Color.FromRgb(200, 100, 40),
        [".dll"] = Color.FromRgb(200, 100, 40),
        // VMs / Disk images
        [".vdi"] = Color.FromRgb(160, 40, 40), [".vmdk"] = Color.FromRgb(160, 40, 40),
        [".vhd"] = Color.FromRgb(160, 40, 40), [".vhdx"] = Color.FromRgb(160, 40, 40),
        [".iso"] = Color.FromRgb(160, 40, 40),
    };

    protected static readonly Color RecycleBinColor = Color.FromRgb(180, 40, 40);
    protected static readonly Color SelectedHighlightColor = Color.FromArgb(90, 255, 255, 255);

    protected ChartBase()
    {
        _hoverThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _hoverThrottle.Tick += (_, _) =>
        {
            _hoverThrottle.Stop();
            if (_hoverDirty)
            {
                _hoverDirty = false;
                var oldHovered = HoveredNode;
                HoveredNode = null;
                InvalidateVisual();
                if (HoveredNode != oldHovered)
                {
                    HoverChanged?.Invoke(HoveredNode);
                    UpdateTooltip();
                }
            }
        };

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _mousePosition = e.GetPosition(this);
        _hoverDirty = true;
        if (!_hoverThrottle.IsEnabled) _hoverThrottle.Start();
    }

    private void UpdateTooltip()
    {
        if (HoveredNode != null)
        {
            var node = HoveredNode;
            string name = node.IsRecycleBin ? "Recycle Bin" : node.Name;
            ToolTip = $"{name}\n{FormatSize(node.TotalSize)}  ({node.TotalFileCount:N0} files)\n{node.FullPath}";
        }
        else
        {
            ToolTip = null;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _mousePosition = null;
        _hoverThrottle.Stop();
        _hoverDirty = false;
        HoveredNode = null;
        InvalidateVisual();
        HoverChanged?.Invoke(null);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (HoveredNode == null && _mousePosition.HasValue)
        {
            _hoverDirty = false;
            HoveredNode = null;
            InvalidateVisual();
        }

        if (HoveredNode != null)
        {
            SelectedNode = HoveredNode;
            NodeClicked?.Invoke(HoveredNode);
            InvalidateVisual();
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (HoveredNode == null || HoveredNode.IsFile) return;

        var node = HoveredNode;
        var menu = new ContextMenu();

        var zoomItem = new MenuItem { Header = $"Hineinzoomen: {node.Name}" };
        zoomItem.Click += (_, _) => ZoomRequested?.Invoke(node);
        menu.Items.Add(zoomItem);

        if (RootNode != ScanRoot && ScanRoot != null)
        {
            var backItem = new MenuItem { Header = "Zurueck zur Uebersicht" };
            backItem.Click += (_, _) => ZoomRequested?.Invoke(ScanRoot!);
            menu.Items.Add(backItem);
        }

        menu.Items.Add(new Separator());

        var explorerItem = new MenuItem { Header = "Im Explorer oeffnen" };
        explorerItem.Click += (_, _) => OpenInExplorer(node.FullPath);
        menu.Items.Add(explorerItem);

        if (node.IsRecycleBin)
        {
            var emptyBin = new MenuItem { Header = "Papierkorb leeren" };
            emptyBin.Click += (_, _) => EmptyRecycleBin();
            menu.Items.Add(emptyBin);
        }

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = $"Verzeichnis loeschen: {node.Name}", Foreground = System.Windows.Media.Brushes.IndianRed };
        deleteItem.Click += (_, _) => DeleteRequested?.Invoke(node);
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
        ContextMenu = menu;
        e.Handled = true;
    }

    private static void EmptyRecycleBin()
    {
        var result = MessageBox.Show(
            "Papierkorb wirklich leeren?\n\nAlle Dateien im Papierkorb werden endgueltig geloescht.",
            "Papierkorb leeren", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                // SHEmptyRecycleBin via shell
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rd /s /q C:\\$Recycle.Bin 2>nul",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                MessageBox.Show("Papierkorb wird geleert. Beim naechsten Scan wird der Platz aktualisiert.",
                    "Papierkorb", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public static void OpenInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }

    protected Color GetNodeColor(DirectoryNode node, int depth, double angle)
    {
        if (node.IsRecycleBin) return RecycleBinColor;

        switch (ColorMode)
        {
            case ColorMode.FileType:
                return GetFileTypeColor(node, depth, angle);
            case ColorMode.Age:
                return GetAgeColor(node);
            default:
                return GetColorForDepth(depth, angle);
        }
    }

    private Color GetFileTypeColor(DirectoryNode node, int depth, double angle)
    {
        // For directories, try to determine dominant file type
        if (!string.IsNullOrEmpty(node.Name))
        {
            var ext = Path.GetExtension(node.Name);
            if (!string.IsNullOrEmpty(ext) && FileTypeColors.TryGetValue(ext, out var c))
                return c;
        }
        return GetColorForDepth(depth, angle);
    }

    private Color GetAgeColor(DirectoryNode node)
    {
        // Try to get last write time of the directory
        try
        {
            var lastWrite = Directory.GetLastWriteTime(node.FullPath);
            var ageYears = (DateTime.Now - lastWrite).TotalDays / 365.0;

            if (ageYears < 0.25) return Color.FromRgb(50, 200, 80);    // Fresh green
            if (ageYears < 1) return Color.FromRgb(120, 200, 50);       // Yellow-green
            if (ageYears < 2) return Color.FromRgb(200, 200, 40);       // Yellow
            if (ageYears < 5) return Color.FromRgb(220, 140, 30);       // Orange
            return Color.FromRgb(200, 50, 50);                           // Old = Red
        }
        catch
        {
            return Color.FromRgb(80, 80, 100);
        }
    }

    protected Color GetColorForDepth(int depth, double angle)
    {
        int idx = ((int)(angle / 30.0) + depth) % Palette.Length;
        if (idx < 0) idx += Palette.Length;

        var baseColor = Palette[idx];
        double factor = 1.0 - depth * 0.08;
        factor = Math.Clamp(factor, 0.4, 1.0);

        return Color.FromRgb(
            (byte)(baseColor.R * factor),
            (byte)(baseColor.G * factor),
            (byte)(baseColor.B * factor));
    }

    protected static DirectoryNode[] SafeGetChildren(DirectoryNode node)
    {
        try { return node.Children.ToArray(); }
        catch { return []; }
    }

    protected static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }
}
