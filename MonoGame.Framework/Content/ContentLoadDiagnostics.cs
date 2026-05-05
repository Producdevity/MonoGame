// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Microsoft.Xna.Framework.Content
{
    internal static class ContentLoadDiagnostics
    {
        private static readonly string ProfileLogPath = ResolveProfileLogPath();
        private static readonly bool ProfileEnabled = !string.IsNullOrEmpty(ProfileLogPath);
        private static readonly bool LogMemoryCacheHits = IsEnabled("SDV_CONTENT_PROFILE_CACHE_HITS");
        private static readonly string CacheRoot = ResolveCacheRoot();
        private static readonly object LogLock = new object();
        private static bool _cacheFailureLogged;
        private static bool _profileFailureLogged;

        [ThreadStatic]
        private static Stack<LoadSample> _samples;

        internal static LoadSample BeginLoad(string assetName, Type targetType, string rootDirectory)
        {
            if (!ProfileEnabled)
                return null;

            if (_samples == null)
                _samples = new Stack<LoadSample>();

            var sample = new LoadSample
            {
                AssetName = assetName,
                TargetType = targetType != null ? targetType.FullName : string.Empty,
                RootDirectory = rootDirectory,
                StartTicks = Stopwatch.GetTimestamp()
            };
            _samples.Push(sample);
            return sample;
        }

        internal static void EndLoad(LoadSample sample, bool success, Exception exception, bool memoryCacheHit)
        {
            if (sample == null)
                return;

            RemoveSample(sample);

            if (memoryCacheHit && !LogMemoryCacheHits)
                return;

            if (string.IsNullOrEmpty(ProfileLogPath))
                return;

            sample.EndTicks = Stopwatch.GetTimestamp();
            sample.Success = success;
            sample.MemoryCacheHit = memoryCacheHit;
            if (exception != null)
                sample.Error = exception.GetType().Name + ": " + exception.Message;

            WriteSample(sample);
        }

        internal static bool TryOpenCachedStream(string assetPath, out Stream stream)
        {
            stream = null;
            if (string.IsNullOrEmpty(CacheRoot))
                return false;

            var cachePath = ResolveCachedPath(assetPath);
            if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
                return false;

            try
            {
                stream = File.OpenRead(cachePath);
                MarkStreamOpened(assetPath, cachePath, true);
                return true;
            }
            catch (Exception ex)
            {
                LogCacheFailure(cachePath, ex);
                stream = null;
                return false;
            }
        }

        internal static void MarkStreamOpened(string assetPath, string sourcePath, bool fromCache)
        {
            var sample = CurrentSample;
            if (sample == null)
                return;

            sample.AssetPath = assetPath;
            sample.StreamSource = sourcePath;
            sample.FromContentCache = fromCache;
        }

        internal static void MarkXnb(byte flags, bool compressedLzx, bool compressedLz4, int xnbLength, int decompressedSize, int compressedSize)
        {
            var sample = CurrentSample;
            if (sample == null)
                return;

            sample.XnbFlags = flags;
            sample.CompressedLzx = compressedLzx;
            sample.CompressedLz4 = compressedLz4;
            sample.XnbLength = xnbLength;
            sample.DecompressedSize = decompressedSize;
            sample.CompressedSize = compressedSize;
        }

        private static LoadSample CurrentSample
        {
            get
            {
                if (_samples == null || _samples.Count == 0)
                    return null;
                return _samples.Peek();
            }
        }

        private static void RemoveSample(LoadSample sample)
        {
            if (_samples == null || _samples.Count == 0)
                return;

            if (object.ReferenceEquals(_samples.Peek(), sample))
            {
                _samples.Pop();
                return;
            }

            var pending = _samples.ToArray();
            _samples.Clear();
            for (var i = pending.Length - 1; i >= 0; i--)
            {
                if (!object.ReferenceEquals(pending[i], sample))
                    _samples.Push(pending[i]);
            }
        }

        private static void WriteSample(LoadSample sample)
        {
            if (_profileFailureLogged)
                return;

            try
            {
                var directory = Path.GetDirectoryName(ProfileLogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                lock (LogLock)
                {
                    var exists = File.Exists(ProfileLogPath);
                    var needsHeader = !exists || new FileInfo(ProfileLogPath).Length == 0;
                    using (var writer = new StreamWriter(ProfileLogPath, true))
                    {
                        if (needsHeader)
                        {
                            writer.WriteLine("utc,stopwatch_ticks,elapsed_ms,asset_name,target_type,root_directory,asset_path,stream_source,memory_cache_hit,content_cache_hit,compressed_lzx,compressed_lz4,xnb_flags,xnb_length,decompressed_size,compressed_size,success,error");
                        }

                        writer.WriteLine(string.Join(",", new[]
                        {
                            Escape(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                            Escape(sample.EndTicks.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.ElapsedMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.AssetName),
                            Escape(sample.TargetType),
                            Escape(sample.RootDirectory),
                            Escape(sample.AssetPath),
                            Escape(sample.StreamSource),
                            Escape(sample.MemoryCacheHit ? "1" : "0"),
                            Escape(sample.FromContentCache ? "1" : "0"),
                            Escape(sample.CompressedLzx ? "1" : "0"),
                            Escape(sample.CompressedLz4 ? "1" : "0"),
                            Escape("0x" + sample.XnbFlags.ToString("X2", CultureInfo.InvariantCulture)),
                            Escape(sample.XnbLength.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.DecompressedSize.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.CompressedSize.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.Success ? "1" : "0"),
                            Escape(sample.Error)
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                _profileFailureLogged = true;
                Console.Out.WriteLine("Content load profiling disabled: {0}", ex.Message);
            }
        }

        private static string ResolveCachedPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
                return null;

            var normalized = assetPath.Replace('\\', '/');
            var parts = normalized.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "..")
                    return null;
            }

            var relativePath = normalized.Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(CacheRoot, relativePath));
            var root = EnsureTrailingSeparator(Path.GetFullPath(CacheRoot));
            if (!candidate.StartsWith(root, StringComparison.Ordinal))
                return null;

            return candidate;
        }

        private static string ResolveProfileLogPath()
        {
            var value = Environment.GetEnvironmentVariable("SDV_CONTENT_PROFILE_LOG");
            if (string.IsNullOrWhiteSpace(value) || value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return null;

            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Environment.GetEnvironmentVariable("SDV_CONTENT_PROFILE_LOG_DIR");
                if (string.IsNullOrWhiteSpace(directory))
                    directory = Path.Combine(AppContext.BaseDirectory, "..", "logs");

                return Path.Combine(directory, "content-load-latest.csv");
            }

            return value;
        }

        private static string ResolveCacheRoot()
        {
            var value = Environment.GetEnvironmentVariable("SDV_CONTENT_CACHE_DIR");
            if (string.IsNullOrWhiteSpace(value) || value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.GetFullPath(value);
        }

        private static void LogCacheFailure(string cachePath, Exception exception)
        {
            if (_cacheFailureLogged)
                return;

            _cacheFailureLogged = true;
            Console.Out.WriteLine("Content cache disabled for {0}: {1}", cachePath, exception.Message);
        }

        private static bool IsEnabled(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return value == "1" ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        private static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal sealed class LoadSample
        {
            public string AssetName;
            public string TargetType;
            public string RootDirectory;
            public string AssetPath;
            public string StreamSource;
            public long StartTicks;
            public long EndTicks;
            public bool MemoryCacheHit;
            public bool FromContentCache;
            public bool CompressedLzx;
            public bool CompressedLz4;
            public byte XnbFlags;
            public int XnbLength;
            public int DecompressedSize;
            public int CompressedSize;
            public bool Success;
            public string Error;

            public double ElapsedMilliseconds
            {
                get { return (EndTicks - StartTicks) * 1000.0 / Stopwatch.Frequency; }
            }
        }
    }
}
