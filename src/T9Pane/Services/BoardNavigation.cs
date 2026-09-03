namespace T9Pane.Services;

internal enum KeyboardSurface
{
    Pinyin,
    Pinyin26,
    English,
    Full,
    Number,
    SymbolCn,
    SymbolEn
}

/// <summary>
/// 拼音、英文、全键都是来源页。数字和符号是临时盘，
/// 中/EN 和返回回到进来之前的那一页。
/// </summary>
internal static class BoardNavigation
{
    public static bool UpdatesHome(KeyboardSurface board) =>
        board is KeyboardSurface.Pinyin
            or KeyboardSurface.Pinyin26
            or KeyboardSurface.English
            or KeyboardSurface.Full;

    public static KeyboardSurface LanguageOrHome(
        KeyboardSurface current,
        KeyboardSurface home)
    {
        if (current is KeyboardSurface.Pinyin or KeyboardSurface.Pinyin26)
        {
            return KeyboardSurface.English;
        }

        if (current == KeyboardSurface.English)
        {
            return home is KeyboardSurface.Pinyin or KeyboardSurface.Pinyin26
                ? home
                : KeyboardSurface.Pinyin;
        }

        return UpdatesHome(home) ? home : KeyboardSurface.Pinyin;
    }

    public static KeyboardSurface BackFromTool(KeyboardSurface home) =>
        UpdatesHome(home) ? home : KeyboardSurface.Pinyin;
}
