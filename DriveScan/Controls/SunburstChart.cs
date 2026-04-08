using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DriveScan.Models;

namespace DriveScan.Controls;

public class SunburstChart : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (RootNode == null) return;

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double maxRadius = size / 2 - 4;
        int maxRings = MaxDepth > 0 ? MaxDepth : 6;
        double ringWidth = maxRadius / (maxRings + 1.2);
        double centerRadius = ringWidth * 1.0;

        var totalScanned = RootNode.TotalSize;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceNormal = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (totalScanned <= 0) return;

        // Draw rings first, then overlay center text (so we know HoveredNode)
        long driveUsed = DriveTotalBytes > 0 ? DriveTotalBytes - DriveFreeBytes : 0;
        double usedSweep = 360.0;
        if (DriveTotalBytes > 0 && driveUsed > 0)
        {
            usedSweep = (double)driveUsed / DriveTotalBytes * 360.0;
            usedSweep = Math.Clamp(usedSweep, 1, 360);
            if (usedSweep < 359)
                DrawArcSegment(dc, center, centerRadius, centerRadius + ringWidth,
                    usedSweep, 360.0 - usedSweep, Color.FromRgb(35, 35, 50));
        }

        DrawRing(dc, center, RootNode, 0, usedSweep, 0, maxRings, centerRadius, ringWidth);

        // Re-draw center circle on top of rings
        dc.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(40, 40, 55)),
            new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 90)), 1),
            center, centerRadius, centerRadius);

        // Center text: always show root name + size
        string rootName = RootNode.Name;
        if (rootName.Length > 18) rootName = rootName[..16] + "..";

        var ftName = MakeText(rootName, centerRadius > 40 ? 13 : 10, typeface, Brushes.White);
        ftName.TextAlignment = TextAlignment.Center;
        ftName.MaxTextWidth = centerRadius * 1.8;
        ftName.MaxLineCount = 1;
        ftName.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(ftName, new Point(center.X, center.Y - 18));

        var ftSize = MakeText(FormatSize(totalScanned), centerRadius > 40 ? 11 : 9, typefaceNormal,
            new SolidColorBrush(Color.FromRgb(170, 190, 220)));
        ftSize.TextAlignment = TextAlignment.Center;
        dc.DrawText(ftSize, new Point(center.X, center.Y + 2));
    }

    private static FormattedText MakeText(string text, double fontSize, Typeface typeface, Brush brush)
    {
        return new FormattedText(text,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush, 1.0);
    }

    private void DrawRing(DrawingContext dc, Point center, DirectoryNode node,
        double startAngle, double sweepAngle, int depth, int maxRings,
        double innerRadiusBase, double ringWidth)
    {
        if (depth >= maxRings) return;
        if (sweepAngle < 0.3) return;

        var children = SafeGetChildren(node);
        Array.Sort(children, (a, b) => b.TotalSize.CompareTo(a.TotalSize));

        long totalSize = node.TotalSize;
        if (totalSize <= 0) return;

        double currentAngle = startAngle;
        double innerRadius = innerRadiusBase + depth * ringWidth;
        double outerRadius = innerRadius + ringWidth;

        if (node.OwnSize > 0 && children.Length > 0)
        {
            double fileSweep = (double)node.OwnSize / totalSize * sweepAngle;
            if (fileSweep >= 0.3)
            {
                DrawArcSegment(dc, center, innerRadius, outerRadius, currentAngle, fileSweep,
                    GetColorForDepth(depth, currentAngle));

                // Make own-files segment hoverable/clickable
                if (_mousePosition.HasValue &&
                    IsPointInArc(_mousePosition.Value, center, innerRadius, outerRadius, currentAngle, fileSweep))
                {
                    HoveredNode = node; // hovering the parent node's files
                }

                currentAngle += fileSweep;
            }
        }

        foreach (var child in children)
        {
            long childSize = child.TotalSize;
            if (childSize <= 0) continue;

            double childSweep = (double)childSize / totalSize * sweepAngle;
            if (childSweep < 0.3) continue;

            var color = GetNodeColor(child, depth, currentAngle);
            bool dashed = child.IsRecycleBin;
            DrawArcSegment(dc, center, innerRadius, outerRadius, currentAngle, childSweep, color, dashed);

            // Highlight selected node
            if (SelectedNode != null && ReferenceEquals(child, SelectedNode))
            {
                DrawArcSegment(dc, center, innerRadius, outerRadius, currentAngle, childSweep,
                    SelectedHighlightColor);
            }

            if (_mousePosition.HasValue &&
                IsPointInArc(_mousePosition.Value, center, innerRadius, outerRadius, currentAngle, childSweep))
            {
                HoveredNode = child;
            }

            DrawRing(dc, center, child, currentAngle, childSweep, depth + 1, maxRings, innerRadiusBase, ringWidth);
            currentAngle += childSweep;
        }
    }

    private static bool IsPointInArc(Point pt, Point center, double innerR, double outerR,
        double startAngle, double sweepAngle)
    {
        double dx = pt.X - center.X;
        double dy = pt.Y - center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < innerR || dist > outerR) return false;

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        angle = (angle + 90 + 360) % 360;

        if (startAngle + sweepAngle > 360)
            return angle >= startAngle || angle <= (startAngle + sweepAngle) % 360;

        return angle >= startAngle && angle <= startAngle + sweepAngle;
    }

    private static void DrawArcSegment(DrawingContext dc, Point center,
        double innerRadius, double outerRadius,
        double startAngle, double sweepAngle, Color color, bool dashed = false)
    {
        if (sweepAngle >= 359.9)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(center.X, center.Y - outerRadius), true, true);
                ctx.ArcTo(new Point(center.X, center.Y + outerRadius), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true, false);
                ctx.ArcTo(new Point(center.X, center.Y - outerRadius), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(center.X, center.Y - innerRadius), false, false);
                ctx.ArcTo(new Point(center.X, center.Y + innerRadius), new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.ArcTo(new Point(center.X, center.Y - innerRadius), new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(new SolidColorBrush(color), new Pen(Brushes.Black, 0.3), geo);
            return;
        }

        double startRad = (startAngle - 90) * Math.PI / 180.0;
        double endRad = (startAngle + sweepAngle - 90) * Math.PI / 180.0;
        bool isLargeArc = sweepAngle > 180;

        var outerStart = new Point(center.X + outerRadius * Math.Cos(startRad), center.Y + outerRadius * Math.Sin(startRad));
        var outerEnd = new Point(center.X + outerRadius * Math.Cos(endRad), center.Y + outerRadius * Math.Sin(endRad));
        var innerStart = new Point(center.X + innerRadius * Math.Cos(endRad), center.Y + innerRadius * Math.Sin(endRad));
        var innerEnd = new Point(center.X + innerRadius * Math.Cos(startRad), center.Y + innerRadius * Math.Sin(startRad));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, true, true);
            ctx.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            ctx.LineTo(innerStart, true, false);
            ctx.ArcTo(innerEnd, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();

        var pen = dashed
            ? new Pen(Brushes.Yellow, 1.5) { DashStyle = DashStyles.Dash }
            : new Pen(Brushes.Black, 0.3);

        dc.DrawGeometry(new SolidColorBrush(color), pen, geometry);
    }
}
