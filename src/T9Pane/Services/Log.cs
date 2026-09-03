using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace T9Pane.Services;

/// <summary>
/// 日志在按键热路径上被频繁调用，原先每条都 File.AppendAllText 开关文件一次，
/// 直接压在 UI 线程上。这里改为投递到后台单写线程，写线程持有常开句柄。
/// </summary>
internal static class Log
{
    private const int QueueLimit = 8192;

    private static readonly BlockingCollection<string> Queue =
        new(new ConcurrentQueue<string>(), QueueLimit);

    private static int _dropped;

    static Log()
    {
        var worker = new Thread(Pump)
        {
            IsBackground = true,
            Name = "T9Pane.Log"
        };
        worker.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Shutdown()
    {
        try
        {
            Queue.CompleteAdding();
        }
        catch
        {
            // 已经关过了
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (!Queue.TryAdd(line))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            // logging must never crash the overlay
        }
    }

    private static void Pump()
    {
        StreamWriter? writer = null;
        try
        {
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                writer ??= TryOpen();
                if (writer is null)
                {
                    continue;
                }

                try
                {
                    writer.WriteLine(line);
                    var dropped = Interlocked.Exchange(ref _dropped, 0);
                    if (dropped > 0)
                    {
                        writer.WriteLine(
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] "
                            + $"日志队列已满，丢弃 {dropped} 条");
                    }

                    // 队列空了再落盘：一次按键的多条日志合成一次写入。
                    if (Queue.Count == 0)
                    {
                        writer.Flush();
                    }
                }
                catch
                {
                    writer.Dispose();
                    writer = null;
                }
            }
        }
        catch
        {
            // 关闭过程中的竞态，忽略
        }
        finally
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static StreamWriter? TryOpen()
    {
        try
        {
            var stream = new FileStream(
                AppSettings.LogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        }
        catch
        {
            return null;
        }
    }
}
