// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Microsoft.Xna.Framework
{
    internal static class GameLoopDiagnostics
    {
        private static readonly string ProfileLogPath = ResolveProfileLogPath();
        private static readonly bool ProfileEnabled = !string.IsNullOrEmpty(ProfileLogPath);
        private static readonly string SlowFrameLogPath = ResolveSlowFrameLogPath();
        private static readonly bool SlowFrameEnabled = !string.IsNullOrEmpty(SlowFrameLogPath);
        private static readonly bool DiagnosticsEnabled = ProfileEnabled || SlowFrameEnabled;
        private static readonly double SlowFrameThresholdMilliseconds = ResolveSlowFrameThresholdMilliseconds();
        private static readonly object LogLock = new object();
        private static bool _profileFailureLogged;
        private static bool _slowFrameFailureLogged;

        private static long _windowStartTicks;
        private static long _windowEndTicks;
        private static int _frames;
        private static int _draws;
        private static int _updates;
        private static int _slowTicks;
        private static int _verySlowTicks;
        private static int _suppressedDraws;
        private static double _totalTickMilliseconds;
        private static double _totalUpdateMilliseconds;
        private static double _totalDrawMilliseconds;
        private static double _totalDrawBodyMilliseconds;
        private static double _totalEndDrawMilliseconds;
        private static double _maxTickMilliseconds;
        private static double _maxUpdateMilliseconds;
        private static double _maxDrawMilliseconds;
        private static double _maxDrawBodyMilliseconds;
        private static double _maxEndDrawMilliseconds;

        internal static TickSample BeginTick()
        {
            if (!DiagnosticsEnabled)
                return null;

            return new TickSample
            {
                StartTicks = Stopwatch.GetTimestamp()
            };
        }

        internal static long BeginUpdate(TickSample sample)
        {
            return sample == null ? 0 : Stopwatch.GetTimestamp();
        }

        internal static void EndUpdate(TickSample sample, long startTicks)
        {
            if (sample == null)
                return;

            sample.UpdateTicks += Stopwatch.GetTimestamp() - startTicks;
            sample.UpdateCount++;
        }

        internal static long BeginDraw(TickSample sample)
        {
            return sample == null ? 0 : Stopwatch.GetTimestamp();
        }

        internal static void EndDraw(TickSample sample, long startTicks)
        {
            if (sample == null)
                return;

            sample.DrawTicks += Stopwatch.GetTimestamp() - startTicks;
            sample.Drew = true;
        }

        internal static long BeginDrawBody(TickSample sample)
        {
            return sample == null ? 0 : Stopwatch.GetTimestamp();
        }

        internal static void EndDrawBody(TickSample sample, long startTicks)
        {
            if (sample == null)
                return;

            sample.DrawBodyTicks += Stopwatch.GetTimestamp() - startTicks;
        }

        internal static long BeginEndDraw(TickSample sample)
        {
            return sample == null ? 0 : Stopwatch.GetTimestamp();
        }

        internal static void EndEndDraw(TickSample sample, long startTicks)
        {
            if (sample == null)
                return;

            sample.EndDrawTicks += Stopwatch.GetTimestamp() - startTicks;
        }

        internal static void MarkSuppressedDraw(TickSample sample)
        {
            if (sample != null)
                sample.SuppressedDraw = true;
        }

        internal static void EndTick(TickSample sample, bool isRunningSlowly)
        {
            if (sample == null)
                return;

            sample.EndTicks = Stopwatch.GetTimestamp();
            sample.IsRunningSlowly = isRunningSlowly;
            WriteSlowFrameSample(sample);
            WriteWindowSample(sample);
        }

        private static void WriteWindowSample(TickSample sample)
        {
            if (!ProfileEnabled || _profileFailureLogged)
                return;

            lock (LogLock)
            {
                if (_windowStartTicks == 0)
                    _windowStartTicks = sample.StartTicks;

                _windowEndTicks = sample.EndTicks;
                _frames++;
                _updates += sample.UpdateCount;
                if (sample.Drew)
                    _draws++;
                if (sample.SuppressedDraw)
                    _suppressedDraws++;
                if (sample.IsRunningSlowly || sample.TickMilliseconds > 33.333)
                    _slowTicks++;
                if (sample.TickMilliseconds > 100.0)
                    _verySlowTicks++;

                _totalTickMilliseconds += sample.TickMilliseconds;
                _totalUpdateMilliseconds += sample.UpdateMilliseconds;
                _totalDrawMilliseconds += sample.DrawMilliseconds;
                _totalDrawBodyMilliseconds += sample.DrawBodyMilliseconds;
                _totalEndDrawMilliseconds += sample.EndDrawMilliseconds;
                _maxTickMilliseconds = Math.Max(_maxTickMilliseconds, sample.TickMilliseconds);
                _maxUpdateMilliseconds = Math.Max(_maxUpdateMilliseconds, sample.UpdateMilliseconds);
                _maxDrawMilliseconds = Math.Max(_maxDrawMilliseconds, sample.DrawMilliseconds);
                _maxDrawBodyMilliseconds = Math.Max(_maxDrawBodyMilliseconds, sample.DrawBodyMilliseconds);
                _maxEndDrawMilliseconds = Math.Max(_maxEndDrawMilliseconds, sample.EndDrawMilliseconds);

                var windowMilliseconds = (_windowEndTicks - _windowStartTicks) * 1000.0 / Stopwatch.Frequency;
                if (windowMilliseconds < 1000.0)
                    return;

                WriteCurrentWindow(windowMilliseconds);
                ResetWindow();
            }
        }

        private static void WriteCurrentWindow(double windowMilliseconds)
        {
            try
            {
                var directory = Path.GetDirectoryName(ProfileLogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var exists = File.Exists(ProfileLogPath);
                var needsHeader = !exists || new FileInfo(ProfileLogPath).Length == 0;
                using (var writer = new StreamWriter(ProfileLogPath, true))
                {
                    if (needsHeader)
                    {
                        writer.WriteLine("utc,stopwatch_start_ticks,stopwatch_end_ticks,window_ms,frames,draws,updates,fps,avg_tick_ms,max_tick_ms,avg_update_ms,max_update_ms,avg_draw_ms,max_draw_ms,avg_draw_body_ms,max_draw_body_ms,avg_end_draw_ms,max_end_draw_ms,slow_ticks,very_slow_ticks,suppressed_draws");
                    }

                    var fps = _draws * 1000.0 / windowMilliseconds;
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Escape(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                        Escape(_windowStartTicks.ToString(CultureInfo.InvariantCulture)),
                        Escape(_windowEndTicks.ToString(CultureInfo.InvariantCulture)),
                        Escape(windowMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_frames.ToString(CultureInfo.InvariantCulture)),
                        Escape(_draws.ToString(CultureInfo.InvariantCulture)),
                        Escape(_updates.ToString(CultureInfo.InvariantCulture)),
                        Escape(fps.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape((_totalTickMilliseconds / Math.Max(1, _frames)).ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_maxTickMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape((_totalUpdateMilliseconds / Math.Max(1, _frames)).ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_maxUpdateMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape((_totalDrawMilliseconds / Math.Max(1, _frames)).ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_maxDrawMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape((_totalDrawBodyMilliseconds / Math.Max(1, _frames)).ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_maxDrawBodyMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape((_totalEndDrawMilliseconds / Math.Max(1, _frames)).ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_maxEndDrawMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                        Escape(_slowTicks.ToString(CultureInfo.InvariantCulture)),
                        Escape(_verySlowTicks.ToString(CultureInfo.InvariantCulture)),
                        Escape(_suppressedDraws.ToString(CultureInfo.InvariantCulture))
                    }));
                }
            }
            catch (Exception ex)
            {
                _profileFailureLogged = true;
                Console.Out.WriteLine("Frame profiling disabled: {0}", ex.Message);
            }
        }

        private static void ResetWindow()
        {
            _windowStartTicks = 0;
            _windowEndTicks = 0;
            _frames = 0;
            _draws = 0;
            _updates = 0;
            _slowTicks = 0;
            _verySlowTicks = 0;
            _suppressedDraws = 0;
            _totalTickMilliseconds = 0;
            _totalUpdateMilliseconds = 0;
            _totalDrawMilliseconds = 0;
            _totalDrawBodyMilliseconds = 0;
            _totalEndDrawMilliseconds = 0;
            _maxTickMilliseconds = 0;
            _maxUpdateMilliseconds = 0;
            _maxDrawMilliseconds = 0;
            _maxDrawBodyMilliseconds = 0;
            _maxEndDrawMilliseconds = 0;
        }

        private static void WriteSlowFrameSample(TickSample sample)
        {
            if (!SlowFrameEnabled || _slowFrameFailureLogged)
                return;

            if (sample.TickMilliseconds < SlowFrameThresholdMilliseconds &&
                sample.UpdateMilliseconds < SlowFrameThresholdMilliseconds &&
                sample.DrawMilliseconds < SlowFrameThresholdMilliseconds &&
                sample.DrawBodyMilliseconds < SlowFrameThresholdMilliseconds &&
                sample.EndDrawMilliseconds < SlowFrameThresholdMilliseconds)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(SlowFrameLogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                lock (LogLock)
                {
                    var exists = File.Exists(SlowFrameLogPath);
                    var needsHeader = !exists || new FileInfo(SlowFrameLogPath).Length == 0;
                    using (var writer = new StreamWriter(SlowFrameLogPath, true))
                    {
                        if (needsHeader)
                        {
                            writer.WriteLine("utc,stopwatch_start_ticks,stopwatch_end_ticks,tick_ms,update_ms,draw_ms,draw_body_ms,end_draw_ms,updates,drew,suppressed_draw,is_running_slowly,gc0_count,gc1_count,gc2_count,managed_heap_bytes");
                        }

                        writer.WriteLine(string.Join(",", new[]
                        {
                            Escape(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                            Escape(sample.StartTicks.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.EndTicks.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.TickMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.UpdateMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.DrawMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.DrawBodyMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.EndDrawMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)),
                            Escape(sample.UpdateCount.ToString(CultureInfo.InvariantCulture)),
                            Escape(sample.Drew ? "1" : "0"),
                            Escape(sample.SuppressedDraw ? "1" : "0"),
                            Escape(sample.IsRunningSlowly ? "1" : "0"),
                            Escape(GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture)),
                            Escape(GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture)),
                            Escape(GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture)),
                            Escape(GC.GetTotalMemory(false).ToString(CultureInfo.InvariantCulture))
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                _slowFrameFailureLogged = true;
                Console.Out.WriteLine("Slow-frame profiling disabled: {0}", ex.Message);
            }
        }

        private static string ResolveProfileLogPath()
        {
            var value = Environment.GetEnvironmentVariable("SDV_PERF_LOG");
            if (string.IsNullOrWhiteSpace(value) || value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return null;

            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Environment.GetEnvironmentVariable("SDV_PERF_LOG_DIR");
                if (string.IsNullOrWhiteSpace(directory))
                    directory = Path.Combine(AppContext.BaseDirectory, "..", "logs");

                return Path.Combine(directory, "perf-latest.csv");
            }

            return value;
        }

        private static string ResolveSlowFrameLogPath()
        {
            var value = Environment.GetEnvironmentVariable("SDV_SLOW_FRAME_LOG");
            if (string.IsNullOrWhiteSpace(value) || value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return null;

            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Environment.GetEnvironmentVariable("SDV_PERF_LOG_DIR");
                if (string.IsNullOrWhiteSpace(directory))
                    directory = Path.Combine(AppContext.BaseDirectory, "..", "logs");

                return Path.Combine(directory, "slow-frame-latest.csv");
            }

            return value;
        }

        private static double ResolveSlowFrameThresholdMilliseconds()
        {
            var value = Environment.GetEnvironmentVariable("SDV_SLOW_FRAME_MS");
            double result;
            if (!string.IsNullOrWhiteSpace(value) &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                result > 0.0)
            {
                return result;
            }

            return 100.0;
        }

        private static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal sealed class TickSample
        {
            public long StartTicks;
            public long EndTicks;
            public long UpdateTicks;
            public long DrawTicks;
            public long DrawBodyTicks;
            public long EndDrawTicks;
            public int UpdateCount;
            public bool Drew;
            public bool SuppressedDraw;
            public bool IsRunningSlowly;

            public double TickMilliseconds
            {
                get { return (EndTicks - StartTicks) * 1000.0 / Stopwatch.Frequency; }
            }

            public double UpdateMilliseconds
            {
                get { return UpdateTicks * 1000.0 / Stopwatch.Frequency; }
            }

            public double DrawMilliseconds
            {
                get { return DrawTicks * 1000.0 / Stopwatch.Frequency; }
            }

            public double DrawBodyMilliseconds
            {
                get { return DrawBodyTicks * 1000.0 / Stopwatch.Frequency; }
            }

            public double EndDrawMilliseconds
            {
                get { return EndDrawTicks * 1000.0 / Stopwatch.Frequency; }
            }
        }
    }
}
