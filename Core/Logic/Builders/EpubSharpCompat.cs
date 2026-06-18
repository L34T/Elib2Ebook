using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using EpubSharp;
using Microsoft.Extensions.Logging;

namespace Core.Logic.Builders;

internal static class EpubSharpCompat
{
    private const string ExpectedPdbMarker = "EpubSharp_Elib2Ebook";
    private const string CacheFileName = "epubsharp-origin.json";

    private static readonly object CacheLock = new();
    private static OriginCacheEntry _originCache;

    private static readonly Lazy<Func<EpubWriter, string, bool>> TrySetSeriesUrlInvoker =
        new(CreateTrySetSeriesUrlInvoker);

    private static readonly Lazy<Func<EpubWriter, string, string, bool>> TryAddNcxWarningPageInvoker =
        new(CreateTryAddNcxWarningPageInvoker);

    public static bool TrySetSeriesUrlIfSupported(EpubWriter writer, string seriesUrl, ILogger logger)
    {
        if (writer == null) return false;
        if (string.IsNullOrWhiteSpace(seriesUrl)) return false;

        if (!IsEpubSharpElib2EbookBuild(writer.GetType().Assembly, logger)) return false;

        var invoker = TrySetSeriesUrlInvoker.Value;
        if (invoker == null) return false;

        try
        {
            return invoker(writer, seriesUrl);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "EpubSharp TrySetSeriesUrl failed; skipping series url.");
            return false;
        }
    }

    public static bool TryAddNcxWarningPageIfSupported(EpubWriter writer, string title, string xhtml, ILogger logger)
    {
        if (writer == null) return false;
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (string.IsNullOrWhiteSpace(xhtml)) return false;

        var invoker = TryAddNcxWarningPageInvoker.Value;
        if (invoker == null) return false;

        try
        {
            return invoker(writer, title, xhtml);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "EpubSharp TryAddNcxWarningPage failed; skipping ncx warning.");
            return false;
        }
    }

    private static Func<EpubWriter, string, bool> CreateTrySetSeriesUrlInvoker()
    {
        var method = typeof(EpubWriter).GetMethod(
            "TrySetSeriesUrl",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(string)],
            null);

        if (method == null) return null;
        if (method.ReturnType != typeof(bool)) return null;

        try
        {
            return method.CreateDelegate<Func<EpubWriter, string, bool>>();
        }
        catch
        {
            return null;
        }
    }

    private static Func<EpubWriter, string, string, bool> CreateTryAddNcxWarningPageInvoker()
    {
        var method = typeof(EpubWriter).GetMethod(
            "TryAddNcxWarningPage",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(string), typeof(string)],
            null);

        if (method == null) return null;
        if (method.ReturnType != typeof(bool)) return null;

        try
        {
            return method.CreateDelegate<Func<EpubWriter, string, string, bool>>();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsEpubSharpElib2EbookBuild(Assembly epubSharpAssembly, ILogger logger)
    {
        var (mvid, size, dllPath) = GetAssemblyFingerprint(epubSharpAssembly);
        if (string.IsNullOrWhiteSpace(dllPath)) return false;

        lock (CacheLock)
        {
            if (_originCache != null &&
                _originCache.Mvid == mvid &&
                _originCache.Size == size)
                return _originCache.IsEpubSharpElib2Ebook;

            var cachePath = GetCachePath();
            var disk = TryReadCache(cachePath);
            if (disk != null && disk.Mvid == mvid && disk.Size == size)
            {
                _originCache = disk;
                return disk.IsEpubSharpElib2Ebook;
            }

            var isExpected = ComputeIsExpectedBuild(dllPath);
            var entry = new OriginCacheEntry
            {
                Mvid = mvid,
                Size = size,
                IsEpubSharpElib2Ebook = isExpected
            };

            _originCache = entry;
            TryWriteCache(cachePath, entry, logger);
            return isExpected;
        }
    }

    private static bool ComputeIsExpectedBuild(string dllPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dllPath);
            if (string.IsNullOrWhiteSpace(dir)) return false;

            var pdbPath = Path.Combine(dir, "EpubSharp.pdb");
            if (!File.Exists(pdbPath)) return false;

            var bytes = File.ReadAllBytes(pdbPath);
            return ContainsAscii(bytes, ExpectedPdbMarker);
        }
        catch
        {
            return false;
        }
    }

    private static (Guid mvid, long size, string path) GetAssemblyFingerprint(Assembly assembly)
    {
        try
        {
            var path = assembly.Location;
            var size = new FileInfo(path).Length;
            var mvid = typeof(EpubWriter).Module.ModuleVersionId;
            return (mvid, size, path);
        }
        catch
        {
            return (Guid.Empty, 0, string.Empty);
        }
    }

    private static string GetCachePath()
    {
        var root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(root))
            root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

        return Path.Combine(root, "Elib2Ebook", CacheFileName);
    }

    private static OriginCacheEntry TryReadCache(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var json = File.ReadAllText(cachePath);
            return JsonSerializer.Deserialize<OriginCacheEntry>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(string cachePath, OriginCacheEntry entry, ILogger logger)
    {
        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(entry);
            File.WriteAllText(cachePath, json);
        }
        catch (Exception ex)
        {
            if (logger != null) logger.LogDebug(ex, "Failed to write EpubSharp origin cache.");
        }
    }

    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        if (haystack.Length == 0) return false;
        if (string.IsNullOrEmpty(needle)) return false;

        var n = needle.Length;
        if (n > haystack.Length) return false;

        for (var i = 0; i <= haystack.Length - n; i++)
        {
            var ok = true;
            for (var j = 0; j < n; j++)
                if (haystack[i + j] != (byte)needle[j])
                {
                    ok = false;
                    break;
                }

            if (ok) return true;
        }

        return false;
    }

    private sealed class OriginCacheEntry
    {
        public Guid Mvid { get; set; }
        public long Size { get; set; }
        public bool IsEpubSharpElib2Ebook { get; set; }
    }
}
