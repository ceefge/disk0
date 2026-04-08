using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DriveScan.Models;

namespace DriveScan.Controls;

public class TreemapChart : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (RootNode == null) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        try
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            int maxDepth = MaxDepth > 0 ? MaxDepth : 6;
            LayoutChildren(dc, RootNode, rect, 0, maxDepth);
        }
        catch
        {
            // Swallow rendering errors during concurrent scan
        }
    }

    private void LayoutChildren(DrawingContext dc, DirectoryNode node, Rect rect, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        if (rect.Width < 3 || rect.Height < 3) return;

        // Safe snapshot
        var childArray = SafeGetChildren(node);
        if (childArray.Length == 0 && node.OwnSize == 0) return;

        // Build flat list of items to draw at this level
        var items = new List<(DirectoryNode Node, long Size)>();

        foreach (var child in childArray)
        {
            long s = child.TotalSize;
            if (s > 0) items.Add((child, s));
        }

        if (node.OwnSize > 0)
        {
            var filesNode = new DirectoryNode
            {
                Name = "[Dateien]",
                FullPath = node.FullPath,
                OwnSize = node.OwnSize,
                IsFile = true
            };
            items.Add((filesNode, node.OwnSize));
        }

        if (items.Count == 0) return;

        // Sort descending
        items.Sort((a, b) => b.Size.CompareTo(a.Size));

        long totalSize = 0;
        foreach (var (_, s) in items) totalSize += s;
        if (totalSize <= 0) return;

        // Slice-and-dice layout
        bool horizontal = rect.Width >= rect.Height;
        double offset = 0;
        double totalDim = horizontal ? rect.Width : rect.Height;

        foreach (var (item, itemSize) in items)
        {
            double fraction = (double)itemSize / totalSize;
            double dim = totalDim * fraction;
            if (dim < 1)
            {
                offset += dim;
                continue;
            }

            Rect childRect;
            if (horizontal)
                childRect = new Rect(rect.X + offset, rect.Y, dim, rect.Height);
            else
                childRect = new Rect(rect.X, rect.Y + offset, rect.Width, dim);

            if (childRect.Width < 1 || childRect.Height < 1)
            {
                offset += dim;
                continue;
            }

            // Draw cell
            var color = GetNodeColor(item, depth, childRect.X + childRect.Y);
            Pen pen = item.IsRecycleBin
                ? new Pen(Brushes.Yellow, 1.5) { DashStyle = DashStyles.Dash }
                : new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 30)), 1);

            dc.DrawRectangle(new SolidColorBrush(color), pen, childRect);

            // Highlight selected
            if (SelectedNode != null && ReferenceEquals(item, SelectedNode))
            {
                dc.DrawRectangle(new SolidColorBrush(SelectedHighlightColor), null, childRect);
            }

            // Hover
            if (_mousePosition.HasValue && childRect.Contains(_mousePosition.Value))
                HoveredNode = item;

            // Label
            if (childRect.Width > 40 && childRect.Height > 16)
            {
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double fontSize = Math.Min(12, Math.Min(childRect.Width / 8, childRect.Height / 2));
                if (fontSize >= 7)
                {
                    string label = item.IsRecycleBin ? "Papierkorb" : item.Name;
                    var ft = new FormattedText(label,
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        typeface, fontSize, Brushes.White, 1.0);
                    ft.MaxTextWidth = Math.Max(1, childRect.Width - 4);
                    ft.MaxLineCount = 1;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(ft, new Point(childRect.X + 2, childRect.Y + 2));
                }
            }

            // Recurse only for real dirs, with strict depth guard
            if (!item.IsFile && depth + 1 <= maxDepth)
            {
                var innerChildren = SafeGetChildren(item);
                if (innerChildren.Length > 0)
                {
                    double headerH = (childRect.Height > 20 && childRect.Width > 40) ? 15 : 0;
                    var innerRect = new Rect(
                        childRect.X + 1, childRect.Y + 1 + headerH,
                        childRect.Width - 2, childRect.Height - 2 - headerH);

                    if (innerRect.Width >= 4 && innerRect.Height >= 4)
                        LayoutChildren(dc, item, innerRect, depth + 1, maxDepth);
                }
            }

            offset += dim;
        }
    }
}
