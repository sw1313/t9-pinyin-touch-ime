using System.IO;
using Microsoft.Win32;

namespace T9Pane.Services;

internal sealed class ImeCatalog
{
    public bool XiaobaiDetected { get; private set; }
    public bool WeaselDetected { get; private set; }
    public bool MicrosoftPinyinDetected { get; private set; }
    public string? XiaobaiDataDirectory { get; private set; }
    public string? RimeUserDirectory { get; private set; }
    public string? WeaselDataDirectory { get; private set; }
    public string Summary { get; private set; } = "未检测到中文输入法词库";

    public IReadOnlyList<string> LexiconDirectories { get; private set; } = [];

    public void Refresh()
    {
        XiaobaiDetected = false;
        WeaselDetected = false;
        MicrosoftPinyinDetected = false;
        XiaobaiDataDirectory = null;
        RimeUserDirectory = null;
        WeaselDataDirectory = null;

        var dirs = new List<string>();
        foreach (var root in CandidateRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var name = root.ToLowerInvariant();
            if (name.Contains("xiaobai") || name.Contains("小白") || File.Exists(Path.Combine(root, "xiaobai.dict.yaml")))
            {
                XiaobaiDetected = true;
                XiaobaiDataDirectory ??= root;
            }

            if (name.Contains("weasel") || name.Contains("小狼毫") || name.EndsWith("\\rime") || name.EndsWith("/rime"))
            {
                WeaselDetected = true;
                if (name.Contains("appdata") && name.EndsWith("rime"))
                {
                    RimeUserDirectory ??= root;
                }
                else
                {
                    WeaselDataDirectory ??= root;
                }
            }

            if (Directory.EnumerateFiles(root, "*.dict.yaml", SearchOption.TopDirectoryOnly).Any()
                || Directory.Exists(Path.Combine(root, "data")) && Directory.EnumerateFiles(Path.Combine(root, "data"), "*.dict.yaml", SearchOption.TopDirectoryOnly).Any())
            {
                dirs.Add(root);
                var data = Path.Combine(root, "data");
                if (Directory.Exists(data))
                {
                    dirs.Add(data);
                    if (File.Exists(Path.Combine(data, "xiaobai.dict.yaml")))
                    {
                        XiaobaiDetected = true;
                        XiaobaiDataDirectory ??= data;
                    }
                }
            }
        }

        MicrosoftPinyinDetected = DetectMicrosoftPinyin();
        var appLexicon = Path.Combine(AppSettings.AppDataDirectory, "lexicon");
        Directory.CreateDirectory(appLexicon);
        dirs.Add(appLexicon);

        LexiconDirectories = dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Summary = BuildSummary();
        Log.Info($"输入法探测：{Summary}；词库目录 {LexiconDirectories.Count} 个");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xiaobai-t9");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "小白T9");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "小白T9");

        foreach (var program in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            foreach (var name in new[] { "小白T9", "XiaobaiT9", "xiaobai-t9", "Rime", "小狼毫", "Weasel" })
            {
                yield return Path.Combine(program, name);
            }

            if (Directory.Exists(program))
            {
                foreach (var dir in Directory.EnumerateDirectories(program, "*Rime*", SearchOption.TopDirectoryOnly))
                {
                    yield return dir;
                    yield return Path.Combine(dir, "data");
                }
            }
        }

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var uninstall in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                     })
            {
                using var key = hive.OpenSubKey(uninstall);
                if (key is null)
                {
                    continue;
                }

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var display = sub?.GetValue("DisplayName") as string ?? "";
                    var location = sub?.GetValue("InstallLocation") as string ?? "";
                    if (location.Length == 0)
                    {
                        continue;
                    }

                    if (display.Contains("小白", StringComparison.OrdinalIgnoreCase)
                        || display.Contains("T9", StringComparison.OrdinalIgnoreCase)
                        || display.Contains("小狼毫", StringComparison.OrdinalIgnoreCase)
                        || display.Contains("Weasel", StringComparison.OrdinalIgnoreCase)
                        || display.Contains("Rime", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return location;
                    }
                }
            }
        }
    }

    private static bool DetectMicrosoftPinyin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\InputMethod");
            if (key?.GetSubKeyNames().Any(x => x.Contains("CHS", StringComparison.OrdinalIgnoreCase) || x.Contains("Pinyin", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "InputMethod"));
    }

    private string BuildSummary()
    {
        var parts = new List<string>();
        if (XiaobaiDetected)
        {
            parts.Add("小白T9");
        }

        if (WeaselDetected)
        {
            parts.Add("小狼毫/Rime");
        }

        if (MicrosoftPinyinDetected)
        {
            parts.Add("微软拼音(词库不可直接读取)");
        }

        return parts.Count == 0 ? "未检测到小白T9/Rime 词库" : string.Join(" + ", parts);
    }
}
