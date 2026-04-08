using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DriveScan.Services;
using Lang = DriveScan.Services.Language;

namespace DriveScan.Views;

public class AnalysisItem : INotifyPropertyChanged
{
    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set { _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); } }
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public string SizeText { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Category { get; init; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class AnalysisWindow : Window
{
    private ObservableCollection<AnalysisItem> _items = new();

    public AnalysisWindow() { InitializeComponent(); }

    private void TitleBar_MouseDown(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

    public void ApplyL10n()
    {
        ColSize.Header = L10n.Get("Size");
        ColDetails.Header = L10n.Get("Details");
        ColCategory.Header = L10n.Get("Category");
        BtnOpenExplorer.Content = L10n.Get("OpenExplorer");
        BtnClose.Content = L10n.Get("Close");
        BtnSelectAll.Content = L10n.Current switch
        {
            Lang.DE => "Alle markieren",
            Lang.FR => "Tout sélectionner",
            Lang.ES => "Seleccionar todo",
            Lang.JA => "全て選択",
            Lang.ZH => "全选",
            Lang.RU => "Выбрать все",
            _ => "Select All"
        };
        BtnDeselectAll.Content = L10n.Current switch
        {
            Lang.DE => "Markierung aufheben",
            Lang.FR => "Tout désélectionner",
            Lang.ES => "Deseleccionar todo",
            Lang.JA => "全て解除",
            Lang.ZH => "取消全选",
            Lang.RU => "Снять выбор",
            _ => "Deselect All"
        };
        BtnDeleteSelected.Content = L10n.Current switch
        {
            Lang.DE => "Markierte loeschen",
            Lang.FR => "Supprimer sélection",
            Lang.ES => "Eliminar seleccionados",
            Lang.JA => "選択を削除",
            Lang.ZH => "删除选中",
            Lang.RU => "Удалить выбранные",
            _ => "Delete Selected"
        };
    }

    public void ShowResults(string title, List<AnalysisResult> results)
    {
        TitleText.Text = title;
        _items = new ObservableCollection<AnalysisItem>(
            results.Select(r => new AnalysisItem
            {
                Path = r.Path, Name = r.Name, Size = r.Size,
                SizeText = r.SizeText, Detail = r.Detail, Category = r.Category
            }));
        ResultsGrid.ItemsSource = _items;
        long totalSize = results.Sum(r => r.Size);
        StatusText.Text = $"{results.Count} {L10n.Get("Results")}";
        SummaryText.Text = $"{L10n.Get("TotalSize")} {AnalysisService.FormatSize(totalSize)}";
        ProgressText.Text = "";
    }

    public void ShowLoading(string title)
    {
        TitleText.Text = title;
        StatusText.Text = L10n.Get("SearchRunning");
        SummaryText.Text = "";
        ProgressText.Text = "";
        ResultsGrid.ItemsSource = null;
    }

    public void UpdateProgress(int filesScanned)
    {
        ProgressText.Text = $"{filesScanned:N0} {L10n.Get("Files")}...";
    }

    private void SelectAll_Click(object s, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsChecked = true;
    }

    private void DeselectAll_Click(object s, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsChecked = false;
    }

    private void DeleteSelected_Click(object s, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsChecked).ToList();
        if (selected.Count == 0) return;

        long totalSize = selected.Sum(i => i.Size);
        string msg = L10n.Current switch
        {
            Lang.DE => $"{selected.Count} Dateien loeschen?\n\nGesamtgroesse: {AnalysisService.FormatSize(totalSize)}\n\nACHTUNG: Nicht rueckgaengig machbar!",
            _ => $"Delete {selected.Count} files?\n\nTotal size: {AnalysisService.FormatSize(totalSize)}\n\nWARNING: Cannot be undone!"
        };

        var r = MessageBox.Show(msg, L10n.Get("DeleteDir"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        int deleted = 0, failed = 0;
        foreach (var item in selected)
        {
            try
            {
                if (File.Exists(item.Path)) { File.Delete(item.Path); deleted++; _items.Remove(item); }
                else if (Directory.Exists(item.Path)) { Directory.Delete(item.Path, true); deleted++; _items.Remove(item); }
                else { _items.Remove(item); deleted++; }
            }
            catch { failed++; }
        }

        string resultMsg = L10n.Current switch
        {
            Lang.DE => $"{deleted} geloescht" + (failed > 0 ? $", {failed} fehlgeschlagen" : ""),
            _ => $"{deleted} deleted" + (failed > 0 ? $", {failed} failed" : "")
        };
        StatusText.Text = resultMsg;
        long remaining = _items.Sum(i => i.Size);
        SummaryText.Text = $"{_items.Count} {L10n.Get("Results")} | {L10n.Get("TotalSize")} {AnalysisService.FormatSize(remaining)}";
    }

    private void ResultsGrid_DoubleClick(object s, MouseButtonEventArgs? e)
    {
        if (ResultsGrid.SelectedItem is AnalysisItem item)
        {
            try
            {
                if (Directory.Exists(item.Path))
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = item.Path, UseShellExecute = true });
                else if (File.Exists(item.Path))
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{item.Path}\"", UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OpenInExplorer_Click(object s, RoutedEventArgs e) => ResultsGrid_DoubleClick(s, null);
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
