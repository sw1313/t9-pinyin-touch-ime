namespace T9Pane.Services;

internal static class LogRetentionPolicy
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public static bool ShouldRotate(long currentLength, long maxBytes = MaxBytes) =>
        currentLength >= maxBytes;

    public static string BackupPath(string path) => path + ".old";
}
