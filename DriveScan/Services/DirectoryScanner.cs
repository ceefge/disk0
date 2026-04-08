using System.IO;
using System.Runtime.InteropServices;
using DriveScan.Models;

namespace DriveScan.Services;

public class DirectoryScanner
{
    private int _directoriesScanned;
    private int _filesScanned;
    private long _bytesScanned;

    public int DirectoriesScanned => _directoriesScanned;
    public int FilesScanned => _filesScanned;
    public long BytesScanned => _bytesScanned;

    private static readonly string[] RecycleBinNames =
        ["$Recycle.Bin", "$RECYCLE.BIN", "RECYCLER"];

    public async Task ScanIntoAsync(DirectoryNode root, int maxDepth, CancellationToken token)
    {
        _directoriesScanned = 0;
        _filesScanned = 0;
        _bytesScanned = 0;
        await Task.Run(() => ScanBreadthFirst(root, maxDepth, token), token);
    }

    private void ScanBreadthFirst(DirectoryNode root, int maxDepth, CancellationToken token)
    {
        ScanSingleDirectory(root, token);
        if (token.IsCancellationRequested) return;

        var currentLevel = new List<DirectoryNode>();
        foreach (var child in root.Children) currentLevel.Add(child);

        for (int depth = 1; depth < maxDepth; depth++)
        {
            if (token.IsCancellationRequested || currentLevel.Count == 0) break;
            foreach (var node in currentLevel)
            {
                if (token.IsCancellationRequested) return;
                ScanSingleDirectory(node, token);
            }
            var nextLevel = new List<DirectoryNode>();
            foreach (var node in currentLevel)
                foreach (var child in node.Children) nextLevel.Add(child);
            currentLevel = nextLevel;
        }
    }

    private void ScanSingleDirectory(DirectoryNode node, CancellationToken token)
    {
        try
        {
            var dirInfo = new DirectoryInfo(node.FullPath);

            // Scan files
            try
            {
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        // For OneDrive cloud-only files, Length may be the actual size
                        // even if not downloaded - this is correct behavior
                        node.OwnSize += file.Length;
                        node.FileCount++;
                        Interlocked.Increment(ref _filesScanned);
                        Interlocked.Add(ref _bytesScanned, file.Length);
                    }
                    catch { }
                }
            }
            catch { }

            // Discover subdirectories
            try
            {
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        // Only skip true symlinks and junctions, NOT OneDrive/cloud placeholders
                        if (IsSymlinkOrJunction(subDir))
                            continue;

                        var childNode = new DirectoryNode
                        {
                            Name = subDir.Name,
                            FullPath = subDir.FullName,
                            Depth = node.Depth + 1,
                            IsRecycleBin = RecycleBinNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase)
                        };

                        node.Children.Add(childNode);
                        Interlocked.Increment(ref _directoriesScanned);
                    }
                    catch { }
                }
            }
            catch { }
        }
        catch { }
    }

    /// <summary>
    /// Returns true only for symlinks and junctions (mount points).
    /// Returns false for OneDrive cloud placeholders and other reparse points.
    /// </summary>
    private static bool IsSymlinkOrJunction(DirectoryInfo dir)
    {
        if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;

        try
        {
            // Check the reparse tag via WIN32_FIND_DATA
            var findData = new WIN32_FIND_DATA();
            var handle = FindFirstFile(dir.FullName, ref findData);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return true; // Can't determine, skip to be safe

            FindClose(handle);

            const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
            const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

            return findData.dwReserved0 == IO_REPARSE_TAG_SYMLINK ||
                   findData.dwReserved0 == IO_REPARSE_TAG_MOUNT_POINT;
        }
        catch
        {
            // If we can't check, skip reparse points to be safe
            return true;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFile(string lpFileName, ref WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr hFindFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0; // reparse tag
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
}
