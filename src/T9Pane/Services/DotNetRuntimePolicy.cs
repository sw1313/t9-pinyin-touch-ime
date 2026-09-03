namespace T9Pane.Services;

/// <summary>
/// 安装程序缺 .NET 8 桌面运行时时按这个顺序拉官方安装包。
/// aka.ms 会跳转，URLMON 在未初始化 COM 的工作线程上经常失败，所以还备了直连 CDN。
/// </summary>
internal static class DotNetRuntimePolicy
{
    public const string Channel = "8.0";
    public const string PinnedVersion = "8.0.30";
    public const long MinInstallerBytes = 20L * 1024 * 1024;

    public static string ChannelUrl(string rid) =>
        $"https://aka.ms/dotnet/{Channel}/windowsdesktop-runtime-{rid}.exe";

    public static string CdnUrl(string rid) =>
        $"https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/{PinnedVersion}/windowsdesktop-runtime-{PinnedVersion}-{rid}.exe";

    public static string AzureCdnUrl(string rid) =>
        $"https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{PinnedVersion}/windowsdesktop-runtime-{PinnedVersion}-{rid}.exe";

    public static string[] DownloadUrls(string rid) =>
        [ChannelUrl(rid), CdnUrl(rid), AzureCdnUrl(rid)];

    public static bool IsValidInstaller(ReadOnlySpan<byte> header, long size) =>
        size >= MinInstallerBytes
        && header.Length >= 2
        && header[0] == (byte)'M'
        && header[1] == (byte)'Z';

    public static bool AcceptInstallerExit(uint code) =>
        code is 0 or 1638 or 3010;
}
