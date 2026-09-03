namespace T9Pane.Services;

internal sealed class KeyboardSkinSetting
{
    public string? Path { get; set; }
    public double Opacity { get; set; } = 0.45;
}

/// <summary>
/// 键盘整体透明、以及按盘面尺寸各用一张不压比例的背景图。
/// 九键 / 数字 / 符号共用窄盘；26 键和英文共用宽盘；全键单独一张。
/// </summary>
internal static class KeyboardSkinPolicy
{
    public const string Compact = "compact";
    public const string English = "english";
    public const string Full = "full";

    public static string Key(bool fullKeyboard, bool wideLetterBoard) =>
        fullKeyboard ? Full : wideLetterBoard ? English : Compact;

    public static string Title(string key) => key switch
    {
        English => "26键 / 英文",
        Full => "全键",
        _ => "九键 / 数字 / 符号"
    };

    public static IReadOnlyList<string> AllKeys { get; } = [Compact, English, Full];

    public static double ClampOverlay(double value) => Math.Clamp(value, 0.25, 1);

    public static double ClampImage(double value) => Math.Clamp(value, 0.05, 1);

    public static KeyboardSkinSetting For(AppSettings settings, string key)
    {
        if (settings.KeyboardSkins.TryGetValue(key, out var skin) && skin is not null)
        {
            skin.Opacity = ClampImage(skin.Opacity <= 0 ? 0.45 : skin.Opacity);
            return skin;
        }

        var created = new KeyboardSkinSetting();
        settings.KeyboardSkins[key] = created;
        return created;
    }
}

internal static class TrayFocusPolicy
{
    public static bool IgnoreOwnProcess(uint pid, uint selfPid) =>
        selfPid != 0 && pid == selfPid;
}

/// <summary>
/// 托盘菜单必须是能激活的窗口：点外面失焦就关。
/// ContextMenu 挂在不激活的隐形窗上时，失焦事件到不了。
/// </summary>
internal static class TrayMenuPolicy
{
    public static bool ShouldDismissOnDeactivate(bool pointerOverMenu) =>
        !pointerOverMenu;

    public static (double Left, double Top) Place(
        double cursorX,
        double cursorY,
        double width,
        double height,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        var left = cursorX;
        var top = cursorY - height;
        if (top < workTop)
        {
            top = cursorY;
        }

        if (left + width > workRight)
        {
            left = workRight - width;
        }

        if (left < workLeft)
        {
            left = workLeft;
        }

        if (top + height > workBottom)
        {
            top = workBottom - height;
        }

        if (top < workTop)
        {
            top = workTop;
        }

        return (left, top);
    }
}
