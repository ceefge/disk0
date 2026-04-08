using System.IO;
using System.Security.Cryptography;

namespace DriveScan.Services;

public record AnalysisResult(string Path, string Name, long Size, string SizeText, string Detail, string Category);

public class AnalysisService
{
    private static readonly HashSet<string> TempFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp", "tmp", "Cache", "Caches", ".cache", "CacheStorage",
        "node_modules", ".git", "bin", "obj", "packages",
        "__pycache__", ".tox", ".pytest_cache", ".mypy_cache",
        "target", "build", "dist", ".gradle", ".nuget",
        "BrowserCache", "Code Cache", "GPUCache", "ShaderCache",
    };

    private static readonly HashSet<string> TempPathParts = new(StringComparer.OrdinalIgnoreCase)
    {
        @"\AppData\Local\Temp", @"\Windows\Temp",
        @"\Windows\SoftwareDistribution\Download", @"\Windows\Installer\$PatchCache$",
        @"\AppData\Local\Google\Chrome\User Data\Default\Cache",
        @"\AppData\Local\Microsoft\Edge\User Data\Default\Cache",
        @"\AppData\Local\Mozilla\Firefox\Profiles", @"\AppData\Local\Docker",
    };

    private static readonly HashSet<string> DeleteCandidateExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".log", ".bak", ".old", ".orig", ".dmp",
        ".thumbs.db", ".ds_store", "desktop.ini",
    };

    public static async Task<List<AnalysisResult>> FindLargestFilesAsync(
        string rootPath, int count, IProgress<int>? progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var top = new List<(string path, long size)>();
            int scanned = 0;
            ScanFilesRecursive(rootPath, (path, size) =>
            {
                if (++scanned % 5000 == 0) progress?.Report(scanned);
                lock (top)
                {
                    top.Add((path, size));
                    if (top.Count % 1000 == 0 && top.Count > count * 2)
                    {
                        top.Sort((a, b) => b.size.CompareTo(a.size));
                        top.RemoveRange(count, top.Count - count);
                    }
                }
            }, token);

            top.Sort((a, b) => b.size.CompareTo(a.size));
            return top.Take(count).Select(v =>
                new AnalysisResult(v.path, Path.GetFileName(v.path), v.size, FormatSize(v.size),
                    Path.GetDirectoryName(v.path) ?? "", "Largest")).ToList();
        }, token);
    }

    public static async Task<List<AnalysisResult>> FindOldFilesAsync(
        string rootPath, int yearsOld, int count, IProgress<int>? progress, CancellationToken token)
    {
        var cutoff = DateTime.Now.AddYears(-yearsOld);
        return await Task.Run(() =>
        {
            var results = new List<AnalysisResult>();
            int scanned = 0;
            ScanFilesRecursive(rootPath, (path, size) =>
            {
                if (++scanned % 5000 == 0) progress?.Report(scanned);
                try
                {
                    var lw = File.GetLastWriteTime(path);
                    if (lw < cutoff && size > 1024 * 1024)
                        lock (results)
                            results.Add(new AnalysisResult(path, Path.GetFileName(path), size,
                                FormatSize(size), $"Last: {lw:dd.MM.yyyy}", $"> {yearsOld}y"));
                }
                catch { }
            }, token);
            return results.OrderByDescending(r => r.Size).Take(count).ToList();
        }, token);
    }

    public static async Task<List<AnalysisResult>> FindTempAndCacheAsync(
        string rootPath, IProgress<int>? progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var results = new List<AnalysisResult>();
            int scanned = 0;
            FindTempDirs(new DirectoryInfo(rootPath), results, 0, ref scanned, progress, token);
            return results.OrderByDescending(r => r.Size).ToList();
        }, token);
    }

    private static void FindTempDirs(DirectoryInfo dir, List<AnalysisResult> results,
        int depth, ref int scanned, IProgress<int>? progress, CancellationToken token)
    {
        if (token.IsCancellationRequested || depth > 8) return;
        try
        {
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (token.IsCancellationRequested) return;
                if (++scanned % 500 == 0) progress?.Report(scanned);
                try
                {
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    bool isTemp = TempFolderNames.Contains(sub.Name);
                    if (!isTemp)
                        foreach (var tp in TempPathParts)
                            if (sub.FullName.Contains(tp, StringComparison.OrdinalIgnoreCase)) { isTemp = true; break; }
                    if (isTemp)
                    {
                        long size = GetDirectorySize(sub);
                        if (size > 10 * 1024 * 1024)
                            results.Add(new AnalysisResult(sub.FullName, sub.Name, size, FormatSize(size), sub.FullName, "Temp/Cache"));
                    }
                    else FindTempDirs(sub, results, depth + 1, ref scanned, progress, token);
                }
                catch { }
            }
        }
        catch { }
    }

    public static async Task<List<AnalysisResult>> FindDuplicatesAsync(
        string rootPath, IProgress<int>? progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            // Phase 1: group by size only (files > 100KB)
            var sizeGroups = new Dictionary<long, List<string>>();
            int scanned = 0;
            ScanFilesRecursive(rootPath, (path, size) =>
            {
                if (++scanned % 5000 == 0) progress?.Report(scanned);
                if (size < 100 * 1024) return;
                lock (sizeGroups)
                {
                    if (!sizeGroups.TryGetValue(size, out var list))
                        sizeGroups[size] = list = new List<string>();
                    list.Add(path);
                }
            }, token);

            // Phase 2: for groups with 2+ files, compare partial hash
            var results = new List<AnalysisResult>();
            foreach (var (size, paths) in sizeGroups)
            {
                if (token.IsCancellationRequested) break;
                if (paths.Count < 2) continue;

                var hashGroups = new Dictionary<string, List<string>>();
                foreach (var path in paths)
                {
                    try
                    {
                        var hash = ComputePartialHash(path);
                        if (!hashGroups.TryGetValue(hash, out var hList))
                            hashGroups[hash] = hList = new List<string>();
                        hList.Add(path);
                    }
                    catch { }
                }

                foreach (var (_, dupes) in hashGroups)
                {
                    if (dupes.Count < 2) continue;
                    long wasted = size * (dupes.Count - 1);
                    foreach (var path in dupes)
                        results.Add(new AnalysisResult(path, Path.GetFileName(path), size,
                            FormatSize(size), $"{dupes.Count}x | {Path.GetDirectoryName(path)}", "Duplicate"));
                }
            }
            return results.OrderByDescending(r => r.Size).Take(500).ToList();
        }, token);
    }

    public static async Task<List<AnalysisResult>> FindDeleteCandidatesAsync(
        string rootPath, IProgress<int>? progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var results = new List<AnalysisResult>();
            int scanned = 0;
            ScanFilesRecursive(rootPath, (path, size) =>
            {
                if (++scanned % 5000 == 0) progress?.Report(scanned);
                var ext = Path.GetExtension(path);
                var name = Path.GetFileName(path);

                string? reason = null;
                if (DeleteCandidateExts.Contains(ext)) reason = "Temp/Backup";
                else if (name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) reason = "Thumbnail cache";
                else if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) reason = "Desktop config";
                else if (name.StartsWith("~$")) reason = "Office temp";
                else if (ext.Equals(".log", StringComparison.OrdinalIgnoreCase) && size > 1024 * 1024) reason = "Large log";

                if (reason != null)
                    lock (results)
                        results.Add(new AnalysisResult(path, name, size, FormatSize(size), reason, "Delete candidate"));
            }, token);
            return results.OrderByDescending(r => r.Size).Take(200).ToList();
        }, token);
    }

    private static string ComputePartialHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[8192];
        int read = stream.Read(buffer, 0, buffer.Length);
        return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
    }

    private static void ScanFilesRecursive(string path, Action<string, long> callback, CancellationToken token)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles())
            {
                if (token.IsCancellationRequested) return;
                try { callback(file.FullName, file.Length); } catch { }
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (token.IsCancellationRequested) return;
                try { if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) ScanFilesRecursive(sub.FullName, callback, token); } catch { }
            }
        }
        catch { }
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        long size = 0;
        try { foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories)) try { size += f.Length; } catch { } } catch { }
        return size;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }
}
