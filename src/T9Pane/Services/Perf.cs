using System.Diagnostics;

namespace T9Pane.Services;

/// <summary>
/// 按键热路径耗时埋点。默认关闭，设 T9PANE_PERF=1 后才采样，
/// 避免为了定位延迟而给正常运行加固定开销。
/// </summary>
internal static class Perf
{
    private static readonly bool On =
        Environment.GetEnvironmentVariable("T9PANE_PERF") is "1" or "true";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bucket> Buckets = [];

    private sealed class Bucket
    {
        public int Count;
        public double TotalMs;
        public double MaxMs;
    }

    public static bool Enabled => On;

    public static Scope Begin(string name) =>
        On ? new Scope(name, Stopwatch.GetTimestamp()) : default;

    public static void Sample(string name, double elapsedMs)
    {
        if (!On)
        {
            return;
        }

        int count;
        double total;
        double max;
        lock (Gate)
        {
            if (!Buckets.TryGetValue(name, out var bucket))
            {
                bucket = new Bucket();
                Buckets[name] = bucket;
            }

            bucket.Count++;
            bucket.TotalMs += elapsedMs;
            bucket.MaxMs = Math.Max(bucket.MaxMs, elapsedMs);
            count = bucket.Count;
            total = bucket.TotalMs;
            max = bucket.MaxMs;
        }

        Log.Info(
            $"耗时 {name} {elapsedMs:F1}ms "
            + $"(第 {count} 次，均 {total / count:F1}ms，峰 {max:F1}ms)");
    }

    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;

        internal Scope(string name, long start)
        {
            _name = name;
            _start = start;
        }

        public void Dispose()
        {
            if (_name is null)
            {
                return;
            }

            var elapsed = Stopwatch.GetTimestamp() - _start;
            Sample(_name, elapsed * 1000.0 / Stopwatch.Frequency);
        }
    }
}
