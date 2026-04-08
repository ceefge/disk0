using System.Collections.Concurrent;

namespace DriveScan.Models;

public class DirectoryNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long OwnSize { get; set; }
    public ConcurrentBag<DirectoryNode> Children { get; } = new();
    public int Depth { get; set; }
    public bool IsFile { get; set; }
    public bool IsRecycleBin { get; set; }
    public bool IsFreeSpace { get; set; }
    public int FileCount { get; set; }

    // Cached values - call UpdateCache() after scan completes
    private long _cachedTotalSize = -1;
    private int _cachedTotalFileCount = -1;

    public long TotalSize
    {
        get
        {
            if (_cachedTotalSize >= 0) return _cachedTotalSize;
            long size = OwnSize;
            try
            {
                foreach (var child in Children)
                    size += child.TotalSize;
            }
            catch { }
            return size;
        }
    }

    public int TotalFileCount
    {
        get
        {
            if (_cachedTotalFileCount >= 0) return _cachedTotalFileCount;
            int count = FileCount;
            try
            {
                foreach (var child in Children)
                    count += child.TotalFileCount;
            }
            catch { }
            return count;
        }
    }

    /// <summary>Recursively cache TotalSize and TotalFileCount. Call after scan is done.</summary>
    public void UpdateCache()
    {
        long size = OwnSize;
        int count = FileCount;
        foreach (var child in Children)
        {
            child.UpdateCache();
            size += child._cachedTotalSize;
            count += child._cachedTotalFileCount;
        }
        _cachedTotalSize = size;
        _cachedTotalFileCount = count;
    }

    public void InvalidateCache()
    {
        _cachedTotalSize = -1;
        _cachedTotalFileCount = -1;
    }
}
