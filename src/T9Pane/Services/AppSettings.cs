using System.IO;
using System.Text.Json;

namespace T9Pane.Services;

internal enum OutputMode
{
    BuiltInT9,
    ForwardToIme
}

internal sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool PreviewMode { get; set; }
    public bool CloakSystemKeyboard { get; set; }
    public OutputMode OutputMode { get; set; } = OutputMode.BuiltInT9;
    public bool ForwardAsNumpad { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool SymbolLock { get; set; }
    public bool EnglishPredict { get; set; } = true;
    public double OverlayOpacity { get; set; } = 1;
    public Dictionary<string, KeyboardSkinSetting> KeyboardSkins { get; set; } = [];
    public List<string> ExtraLexiconDirectories { get; set; } = [];

    public static string AppDataDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "T9Pane");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
    public static string UserLexiconPath => Path.Combine(AppDataDirectory, "user-lexicon.txt");
    public static string LogPath => Path.Combine(AppDataDirectory, "t9pane.log");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (!json.Contains("EnglishPredict", StringComparison.Ordinal))
                {
                    loaded.EnglishPredict = true;
                }

                loaded.OverlayOpacity = KeyboardSkinPolicy.ClampOverlay(
                    loaded.OverlayOpacity <= 0 ? 1 : loaded.OverlayOpacity);
                loaded.KeyboardSkins ??= [];
                loaded.Enabled = true;
                loaded.PreviewMode = false;
                return loaded;
            }
        }
        catch
        {
            // keep defaults
        }

        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        ApplyAutoStart();
    }

    private void ApplyAutoStart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key is null)
            {
                return;
            }

            const string name = "T9Pane";
            if (AutoStart)
            {
                var exe = File.Exists(UiAccessInstall.InstalledExe)
                    ? UiAccessInstall.InstalledExe
                    : (Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "T9Pane.exe"));
                // uiAccess 必须由资源管理器拉起。登录 Run 直接起 exe 经常没有高层权限。
                var command = File.Exists(UiAccessInstall.InstalledExe)
                    ? $"explorer.exe \"{exe}\""
                    : $"\"{exe}\"";
                key.SetValue(name, command);
            }
            else if (key.GetValue(name) is not null)
            {
                key.DeleteValue(name);
            }
        }
        catch
        {
            // ignore registry failures
        }
    }
}
