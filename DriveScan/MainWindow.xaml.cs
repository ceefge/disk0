using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lang = DriveScan.Services.Language;
using DriveScan.Controls;
using DriveScan.Models;
using System.Diagnostics;
using DriveScan.Services;
using DriveScan.Views;

namespace DriveScan;

public record FileEntry(string Name, string SizeText, string DateText, long Size);

public partial class MainWindow : Window
{
    private readonly DirectoryScanner _scanner = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _refreshTimer;
    private DirectoryNode? _rootNode;
    private DirectoryNode? _scanRoot;
    private long _driveTotalBytes, _driveFreeBytes;
    private bool _isScanning;
    private DirectoryNode? _selectedInfoNode;
    private int _fileListMode = 1;
    private AppSettings _settings = null!;
    private string _selectedDrivePath = @"C:\";

    public MainWindow()
    {
        _settings = AppSettings.Load();
        if (!string.IsNullOrEmpty(_settings.Language) && Enum.TryParse<Lang>(_settings.Language, out var lang))
            L10n.Current = lang;

        InitializeComponent();

        if (!_settings.DisclaimerAccepted)
        {
            var r = MessageBox.Show(L10n.Get("Disclaimer") + "\n\n" + L10n.Get("DisclaimerAccept"),
                L10n.Get("DisclaimerTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r != MessageBoxResult.Yes) { Close(); return; }
            _settings.DisclaimerAccepted = true; _settings.Save();
        }

        SunburstView.HoverChanged += OnHoverChanged; TreemapView.HoverChanged += OnHoverChanged;
        SunburstView.NodeClicked += OnNodeClicked; TreemapView.NodeClicked += OnNodeClicked;
        SunburstView.ZoomRequested += OnZoomRequested; TreemapView.ZoomRequested += OnZoomRequested;
        SunburstView.DeleteRequested += OnDeleteRequested; TreemapView.DeleteRequested += OnDeleteRequested;
        Loaded += OnLoaded;
        L10n.LanguageChanged += () => { ApplyL10n(); _settings.Language = L10n.Current.ToString(); _settings.Save(); };
        ShowInfoLeftRoot();
    }

    private void OnLoaded(object s, RoutedEventArgs e)
    {
        PopulateDriveList();
        ApplyL10n();
        MenuAutoScan.IsChecked = _settings.AutoScanOnStart;
        UpdateLangChecks();

        // If launched with --scan argument, scan that path
        if (!string.IsNullOrEmpty(App.StartScanPath))
        {
            AddPathToDriveList(App.StartScanPath);
            StartScan(this, new RoutedEventArgs());
        }
        else if (_settings.AutoScanOnStart)
        {
            StartScan(this, new RoutedEventArgs());
        }
    }

    private void ApplyL10n()
    {
        var L = L10n.Get;
        MenuAnalysis.Header = L("Analysis"); MenuView.Header = L("View");
        MenuHelp.Header = L("Help"); MenuLanguage.Header = L("LanguageMenu");
        MenuTopLargest.Header = L("TopLargestFiles"); MenuOld2.Header = L("OldFiles2");
        MenuOld5.Header = L("OldFiles5"); MenuTemp.Header = L("TempCache");
        MenuDupes.Header = L("Duplicates"); MenuDeleteCand.Header = L("DeleteCandidates");
        MenuColorDefault.Header = L("ColorDefault"); MenuColorFileType.Header = L("ColorFileType");
        MenuColorAge.Header = L("ColorAge"); MenuFileListOff.Header = L("FileListOff");
        MenuFileListSize.Header = L("FileListSize"); MenuFileListSizeDate.Header = L("FileListSizeDate");
        MenuAutoScan.Header = L("AutoScanOnStart");
        MenuRegisterShell.Header = L("RegisterShell");
        MenuUnregisterShell.Header = L("UnregisterShell");
        MenuLegend.Header = L("Legend"); MenuAbout.Header = L("About");
        MenuUserGuide.Header = L("UserGuide");
        LblView.Text = L("ViewMode"); LblDepth.Text = L("DepthLabel");
        CbSunburst.Content = L("Sunburst"); CbTreemap.Content = L("Treemap");
        ScanButton.Content = $"\u25B6 {L("Scan")}"; BackButton.Content = $"\u2190 {L("Back")}";
        StopButton.Content = $"\u25A0 {L("Stop")}";
        BtnAddNetwork.ToolTip = L("AddNetwork");
    }

    #region Title Bar
    private void TitleBar_MouseDown(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { if (e.ClickCount == 2) MaxRestore_Click(s, e); else DragMove(); } }
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxRestore_Click(object s, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; MaxBtn.Content = WindowState == WindowState.Maximized ? "\u2752" : "\u25A1"; }
    private void Close_Click(object s, RoutedEventArgs e) => Close();
    #endregion

    #region Drive List (left panel)
    private void PopulateDriveList()
    {
        DriveListBox.Items.Clear();
        // Add C: first
        DriveListBox.Items.Add(new ListBoxItem { Content = "C:", Tag = @"C:\", FontWeight = FontWeights.Bold });

        _ = Task.Run(() => DriveInfo.GetDrives()
            .Where(d => d.IsReady && !d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Name).ToList())
            .ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var n in t.Result)
                        DriveListBox.Items.Add(new ListBoxItem { Content = n.TrimEnd('\\'), Tag = n });
                });
            });

        DriveListBox.SelectedIndex = 0;
    }

    private void DriveList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (DriveListBox.SelectedItem is ListBoxItem item)
        {
            _selectedDrivePath = item.Tag as string ?? @"C:\";
            // Highlight root drives in orange
            bool isRoot = _selectedDrivePath.Length <= 4; // "C:\" or "D:\"
            foreach (ListBoxItem li in DriveListBox.Items)
                li.BorderThickness = new Thickness(0);
            if (isRoot)
                item.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 160, 40));
            item.BorderThickness = new Thickness(2);
        }
    }

    private void AddNetwork_Click(object s, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var browseItem = new MenuItem { Header = L10n.Current switch
        {
            Lang.DE => "Ordner waehlen...",
            Lang.FR => "Choisir dossier...",
            Lang.ES => "Elegir carpeta...",
            Lang.JA => "フォルダを選択...",
            Lang.ZH => "选择文件夹...",
            Lang.RU => "Выбрать папку...",
            _ => "Browse folder..."
        }};
        browseItem.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select folder" };
            if (dlg.ShowDialog() == true)
                AddPathToDriveList(dlg.FolderName);
        };
        menu.Items.Add(browseItem);

        var networkItem = new MenuItem { Header = L10n.Get("AddNetwork") };
        networkItem.Click += (_, _) =>
        {
            var inputDlg = new InputDialog(L10n.Get("NetworkPath"), L10n.Get("AddNetwork")) { Owner = this };
            if (inputDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDlg.InputText))
                AddPathToDriveList(inputDlg.InputText.Trim());
        };
        menu.Items.Add(networkItem);

        BtnAddNetwork.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void AddPathToDriveList(string path)
    {
        // Shorten display label
        string label = path.Length > 12 ? "..." + path[^12..] : path;
        DriveListBox.Items.Add(new ListBoxItem { Content = label, Tag = path, ToolTip = path });
        DriveListBox.SelectedIndex = DriveListBox.Items.Count - 1;
    }
    #endregion

    #region Language
    private void SetLang(Lang lang) { L10n.Current = lang; UpdateLangChecks(); }
    private void UpdateLangChecks() { LangEN.IsChecked = L10n.Current == Lang.EN; LangDE.IsChecked = L10n.Current == Lang.DE; LangFR.IsChecked = L10n.Current == Lang.FR; LangES.IsChecked = L10n.Current == Lang.ES; LangJA.IsChecked = L10n.Current == Lang.JA; LangZH.IsChecked = L10n.Current == Lang.ZH; LangRU.IsChecked = L10n.Current == Lang.RU; }
    private void LangEN_Click(object s, RoutedEventArgs e) => SetLang(Lang.EN);
    private void LangDE_Click(object s, RoutedEventArgs e) => SetLang(Lang.DE);
    private void LangFR_Click(object s, RoutedEventArgs e) => SetLang(Lang.FR);
    private void LangES_Click(object s, RoutedEventArgs e) => SetLang(Lang.ES);
    private void LangJA_Click(object s, RoutedEventArgs e) => SetLang(Lang.JA);
    private void LangZH_Click(object s, RoutedEventArgs e) => SetLang(Lang.ZH);
    private void LangRU_Click(object s, RoutedEventArgs e) => SetLang(Lang.RU);
    #endregion

    #region Menu - View
    private void UpdateFileListMenuChecks() { MenuFileListOff.IsChecked = _fileListMode == 0; MenuFileListSize.IsChecked = _fileListMode == 1; MenuFileListSizeDate.IsChecked = _fileListMode == 2; }
    private void MenuFileListOff_Click(object s, RoutedEventArgs e) { _fileListMode = 0; UpdateFileListMenuChecks(); FileListPanel.Visibility = Visibility.Collapsed; }
    private void MenuFileListSize_Click(object s, RoutedEventArgs e) { _fileListMode = 1; UpdateFileListMenuChecks(); if (_selectedInfoNode != null) _ = LoadFileListAsync(_selectedInfoNode); }
    private void MenuFileListSizeDate_Click(object s, RoutedEventArgs e) { _fileListMode = 2; UpdateFileListMenuChecks(); if (_selectedInfoNode != null) _ = LoadFileListAsync(_selectedInfoNode); }
    private void MenuAutoScan_Click(object s, RoutedEventArgs e) { _settings.AutoScanOnStart = MenuAutoScan.IsChecked; _settings.Save(); }
    private void MenuRegisterShell_Click(object s, RoutedEventArgs e)
    {
        if (ShellIntegration.Register())
            MessageBox.Show(L10n.Get("ShellRegistered"), "disk0", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(L10n.Get("ShellNeedAdmin"), "disk0", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void MenuUnregisterShell_Click(object s, RoutedEventArgs e)
    {
        if (ShellIntegration.Unregister())
            MessageBox.Show(L10n.Get("ShellUnregistered"), "disk0", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(L10n.Get("ShellNeedAdmin"), "disk0", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void UpdateColorChecks() { MenuColorDefault.IsChecked = SunburstView.ColorMode == ColorMode.Default; MenuColorFileType.IsChecked = SunburstView.ColorMode == ColorMode.FileType; MenuColorAge.IsChecked = SunburstView.ColorMode == ColorMode.Age; }
    private void SetColorMode(ColorMode m) { SunburstView.ColorMode = m; TreemapView.ColorMode = m; UpdateColorChecks(); GetActiveChart().InvalidateVisual(); }
    private void MenuColorDefault_Click(object s, RoutedEventArgs e) => SetColorMode(ColorMode.Default);
    private void MenuColorFileType_Click(object s, RoutedEventArgs e) => SetColorMode(ColorMode.FileType);
    private void MenuColorAge_Click(object s, RoutedEventArgs e) => SetColorMode(ColorMode.Age);
    #endregion

    #region Menu - Analysis
    /// <summary>Analysis runs on selected node's path, or scan root if nothing selected.</summary>
    private string GetAnalysisPath()
    {
        if (_selectedInfoNode != null && !_selectedInfoNode.IsFile)
            return _selectedInfoNode.FullPath;
        return _scanRoot?.FullPath ?? _selectedDrivePath;
    }

    private async void MenuTopLargest_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("TopLargestFiles"), (p, t) => AnalysisService.FindLargestFilesAsync(GetAnalysisPath(), 50, p, t));
    private async void MenuOld2_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("OldFiles2"), (p, t) => AnalysisService.FindOldFilesAsync(GetAnalysisPath(), 2, 100, p, t));
    private async void MenuOld5_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("OldFiles5"), (p, t) => AnalysisService.FindOldFilesAsync(GetAnalysisPath(), 5, 100, p, t));
    private async void MenuTemp_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("TempCache"), (p, t) => AnalysisService.FindTempAndCacheAsync(GetAnalysisPath(), p, t));
    private async void MenuDupes_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("Duplicates"), (p, t) => AnalysisService.FindDuplicatesAsync(GetAnalysisPath(), p, t));
    private async void MenuDeleteCand_Click(object s, RoutedEventArgs e) => await RunAnalysis(L10n.Get("DeleteCandidates"), (p, t) => AnalysisService.FindDeleteCandidatesAsync(GetAnalysisPath(), p, t));

    private async Task RunAnalysis(string title, Func<IProgress<int>, CancellationToken, Task<List<AnalysisResult>>> action)
    {
        var scope = GetAnalysisPath();
        var win = new AnalysisWindow { Owner = this };
        win.ApplyL10n();
        win.ShowLoading($"{title}  [{scope}]");
        win.Show();
        var progress = new Progress<int>(count => win.UpdateProgress(count));
        var results = await action(progress, CancellationToken.None);
        win.ShowResults($"{title}  [{scope}]", results);
    }
    #endregion

    #region Menu - Help
    private void MenuUserGuide_Click(object s, RoutedEventArgs e) => MessageBox.Show(L10n.Get("HelpText"), L10n.Get("HelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void MenuLegend_Click(object s, RoutedEventArgs e) => MessageBox.Show(
        "Video: Red | Images: Green | Audio: Yellow\nDocuments: Blue | Archives: Purple | Executables: Orange\nVM/Disk: Dark Red\n\nAge: Green (<3mo) > Yellow (<2y) > Orange (<5y) > Red (>5y)",
        L10n.Get("Legend"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void MenuAbout_Click(object s, RoutedEventArgs e) => MessageBox.Show(L10n.Get("Disclaimer"), L10n.Get("About"), MessageBoxButton.OK, MessageBoxImage.Information);
    #endregion

    #region Scan
    private async void StartScan(object s, RoutedEventArgs e)
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource(); var token = _cts.Token;
        var drv = _selectedDrivePath;
        if (string.IsNullOrEmpty(drv)) return;
        int maxDepth = int.Parse(((ComboBoxItem)DepthCombo.SelectedItem).Content.ToString()!);

        ScanButton.IsEnabled = false; StopButton.IsEnabled = true; BackButton.IsEnabled = false;
        _isScanning = true; _selectedInfoNode = null; ScanProgressText.Text = "0%"; FileListPanel.Visibility = Visibility.Collapsed;

        _driveTotalBytes = 0; _driveFreeBytes = 0;
        // Only query DriveInfo for real drive roots (e.g. "C:\"), not arbitrary paths
        if (drv.Length <= 4 && char.IsLetter(drv[0]) && drv[1] == ':')
        {
            try { var di = new DriveInfo(drv); if (di.IsReady) { _driveTotalBytes = di.TotalSize; _driveFreeBytes = di.AvailableFreeSpace; } } catch { }
        }
        UpdateDriveInfoBar();
        DriveInfoBar.Visibility = _driveTotalBytes > 0 ? Visibility.Visible : Visibility.Collapsed;

        _rootNode = new DirectoryNode { Name = drv, FullPath = drv, Depth = 0 };
        _scanRoot = _rootNode; SetChartRoot(_rootNode); ClearSelection(); ShowInfoLeftRoot(); StatusPath.Text = drv;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshTimer.Tick += (_, _) => RefreshChart(); _refreshTimer.Start();

        try { await _scanner.ScanIntoAsync(_rootNode, maxDepth, token); await Task.Run(() => _rootNode.UpdateCache()); _scanRoot = _rootNode; }
        catch (OperationCanceledException) { }
        finally
        {
            _refreshTimer?.Stop(); _refreshTimer = null; _isScanning = false; ScanProgressText.Text = "";
            SetChartRoot(_rootNode); RefreshChart();
            ScanButton.IsEnabled = true; StopButton.IsEnabled = false;
            if (_rootNode != null)
            {
                var L = L10n.Get;
                DirCountText.Text = $"{_scanner.DirectoriesScanned:N0} {L("Dirs")}";
                FileCountText.Text = $"{_scanner.FilesScanned:N0} {L("Files")}";
                StatusPath.Text = $"{_rootNode.FullPath}  \u2014  {L("Done")} ({FormatSize(_rootNode.TotalSize)})";
            }
        }
    }
    private void StopScan(object s, RoutedEventArgs e) => _cts?.Cancel();
    private void RefreshChart()
    {
        if (_rootNode == null) return;
        GetActiveChart().InvalidateVisual();
        var L = L10n.Get;
        DirCountText.Text = $"{_scanner.DirectoriesScanned:N0} {L("Dirs")}";
        FileCountText.Text = $"{_scanner.FilesScanned:N0} {L("Files")}";
        if (_isScanning)
        {
            long scanned = _scanner.BytesScanned;
            if (_driveTotalBytes > 0) { long used = _driveTotalBytes - _driveFreeBytes; double pct = used > 0 ? (double)scanned / used * 100 : 0;
                ScanProgressText.Text = L10n.Format("ScanProgress", $"{Math.Min(pct, 99.9):F1}"); }
            else ScanProgressText.Text = $"{L("Scanning")} {FormatSize(scanned)}";
            StatusPath.Text = $"{L("Scanning")} {_rootNode.FullPath}... {FormatSize(scanned)}";
        }
    }
    #endregion

    #region UI
    private void UpdateDriveInfoBar()
    {
        if (_driveTotalBytes > 0)
        { var L = L10n.Get; DriveInfoBar.Visibility = Visibility.Visible; long used = _driveTotalBytes - _driveFreeBytes;
          DriveInfoTotal.Text = $"{L("Total")} {FormatSize(_driveTotalBytes)}";
          DriveInfoUsed.Text = $"{L("Used")} {FormatSize(used)} ({(double)used/_driveTotalBytes*100:F1}%)";
          DriveInfoFree.Text = $"{L("Free")} {FormatSize(_driveFreeBytes)} ({(double)_driveFreeBytes/_driveTotalBytes*100:F1}%)"; }
        else DriveInfoBar.Visibility = Visibility.Collapsed;
    }
    private void ShowInfoLeftForNode(DirectoryNode node)
    { var L = L10n.Get; InfoPath.Text = node.IsRecycleBin ? L("RecycleBin") : node.Name;
      InfoPanelLeft.Visibility = Visibility.Visible; OpenExplorerBtn.Visibility = !node.IsFile ? Visibility.Visible : Visibility.Collapsed; OpenExplorerBtn.Tag = node.FullPath;
      InfoSize.Text = FormatSize(node.TotalSize); InfoFiles.Text = $"{node.TotalFileCount:N0} {L("Files")}";
      try { int d = node.Children.Count(c => !c.IsFile); InfoDirs.Text = d > 0 ? $"{d:N0} {L("Subdirs")}" : ""; } catch { InfoDirs.Text = ""; }
      StatusPath.Text = $"{node.FullPath}  \u2014  {FormatSize(node.TotalSize)}"; }
    private void ShowInfoLeftRoot()
    { var r = GetActiveChart()?.RootNode ?? _rootNode;
      if (r != null) { InfoPath.Text = r.FullPath; InfoSize.Text = FormatSize(r.TotalSize); InfoFiles.Text = ""; InfoDirs.Text = "";
        InfoPanelLeft.Visibility = Visibility.Visible; OpenExplorerBtn.Visibility = Visibility.Visible; OpenExplorerBtn.Tag = r.FullPath; StatusPath.Text = r.FullPath; }
      else InfoPanelLeft.Visibility = Visibility.Collapsed; }
    private void OpenExplorer_Click(object s, RoutedEventArgs e) { var p = OpenExplorerBtn.Tag as string; if (!string.IsNullOrEmpty(p)) ChartBase.OpenInExplorer(p); }
    private void FileListBox_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (FileListBox.SelectedItem is FileEntry entry && _selectedInfoNode != null)
        {
            var filePath = Path.Combine(_selectedInfoNode.FullPath, entry.Name);
            if (File.Exists(filePath))
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{filePath}\"", UseShellExecute = true });
        }
    }
    private void ClearSelection() { _selectedInfoNode = null; SunburstView.SelectedNode = null; TreemapView.SelectedNode = null; }
    private void SetChartRoot(DirectoryNode? node) { SunburstView.RootNode = node; TreemapView.RootNode = node; SunburstView.ScanRoot = _scanRoot; TreemapView.ScanRoot = _scanRoot;
        SunburstView.DriveTotalBytes = _driveTotalBytes; SunburstView.DriveFreeBytes = _driveFreeBytes; TreemapView.DriveTotalBytes = _driveTotalBytes; TreemapView.DriveFreeBytes = _driveFreeBytes;
        BackButton.IsEnabled = node != null && node != _scanRoot; }
    private ChartBase GetActiveChart() => ChartTypeCombo?.SelectedIndex == 1 ? TreemapView : SunburstView;
    private void ChartTypeChanged(object s, SelectionChangedEventArgs e) { if (!IsLoaded) return; bool sb = ChartTypeCombo.SelectedIndex == 0;
        SunburstView.Visibility = sb ? Visibility.Visible : Visibility.Collapsed; TreemapView.Visibility = sb ? Visibility.Collapsed : Visibility.Visible;
        GetActiveChart().SelectedNode = _selectedInfoNode; if (_rootNode != null) GetActiveChart().InvalidateVisual(); }
    private void DepthChanged(object s, SelectionChangedEventArgs e) { if (!IsLoaded || DepthCombo?.SelectedItem == null) return;
        int d = int.Parse(((ComboBoxItem)DepthCombo.SelectedItem).Content.ToString()!); SunburstView.MaxDepth = d; TreemapView.MaxDepth = d;
        if (_rootNode != null) GetActiveChart().InvalidateVisual(); }
    #endregion

    #region Chart Events
    private void OnHoverChanged(DirectoryNode? node) { if (node != null) ShowInfoLeftForNode(node);
        else if (!_isScanning) { if (_selectedInfoNode != null) ShowInfoLeftForNode(_selectedInfoNode); else ShowInfoLeftRoot(); } }
    private void OnNodeClicked(DirectoryNode node) { _selectedInfoNode = node; SunburstView.SelectedNode = node; TreemapView.SelectedNode = node;
        ShowInfoLeftForNode(node); if (_fileListMode > 0) _ = LoadFileListAsync(node); }
    private async Task LoadFileListAsync(DirectoryNode node)
    { if (node.IsFile || _fileListMode == 0) { FileListPanel.Visibility = Visibility.Collapsed; return; }
      var L = L10n.Get; FileListTitle.Text = $"{L("Files")}: {node.Name}"; FileListPanel.Visibility = Visibility.Visible; FileListBox.ItemsSource = null;
      var path = node.FullPath; bool date = _fileListMode == 2;
      var files = await Task.Run(() => { try { return new DirectoryInfo(path).EnumerateFiles()
          .Select(f => { try { return new FileEntry(f.Name, FormatSize(f.Length), date ? f.LastWriteTime.ToString("dd.MM.yyyy HH:mm") : "", f.Length); } catch { return null; } })
          .Where(f => f != null).OrderByDescending(f => f!.Size).Take(200).ToList(); } catch { return new List<FileEntry?>(); } });
      if (_selectedInfoNode == node) FileListBox.ItemsSource = files; }
    private void OnZoomRequested(DirectoryNode node) { ClearSelection(); SetChartRoot(node); ShowInfoLeftRoot(); FileListPanel.Visibility = Visibility.Collapsed; GetActiveChart().InvalidateVisual(); }
    private void OnDeleteRequested(DirectoryNode node)
    { var L = L10n.Get;
      var r = MessageBox.Show($"{L("DeleteConfirm")}\n\n{node.FullPath}\n\n{FormatSize(node.TotalSize)}\n{node.TotalFileCount:N0} {L("Files")}\n\n{L("DeleteWarning")}",
          L("DeleteDir") + node.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning);
      if (r == MessageBoxResult.Yes) { try { Directory.Delete(node.FullPath, true); MessageBox.Show(L("DeletedRescan"), L("Deleted"), MessageBoxButton.OK, MessageBoxImage.Information); }
          catch (Exception ex) { MessageBox.Show($"{L("ErrorDeleting")}\n{ex.Message}", L("Error"), MessageBoxButton.OK, MessageBoxImage.Error); } } }
    private void GoBack(object s, RoutedEventArgs e) { if (_scanRoot != null) { ClearSelection(); SetChartRoot(_scanRoot); ShowInfoLeftRoot(); FileListPanel.Visibility = Visibility.Collapsed; GetActiveChart().InvalidateVisual(); } }
    #endregion

    private static string FormatSize(long bytes)
    { if (bytes < 1024) return $"{bytes} B"; if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
      if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
      if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
      return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB"; }
}
