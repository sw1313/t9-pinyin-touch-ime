namespace T9Pane.Services;

/// <summary>
/// 全键盘虚拟键。修饰键行为见 <see cref="TouchModifierPolicy"/>：
/// 传统布局点一下挂上，再点一下解除；和下一个键组成快捷键。
/// </summary>
internal static class FullKeyVirtuals
{
    public static bool HasCommandMods(bool ctrl, bool alt, bool win) =>
        ctrl || alt || win;

    public static IReadOnlyList<ushort> StickyModifiers(
        bool ctrl,
        bool alt,
        bool shift,
        bool win)
    {
        var mods = new List<ushort>(4);
        if (ctrl)
        {
            mods.Add(0x11);
        }

        if (alt)
        {
            mods.Add(0x12);
        }

        if (shift)
        {
            mods.Add(0x10);
        }

        if (win)
        {
            mods.Add(0x5B);
        }

        return mods;
    }

    public static bool IsExtended(ushort vk) =>
        vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x5B;

    public static ushort? Of(FullKeySpec spec)
    {
        if (spec.Action == FullKeyAction.Letter)
        {
            return LetterVk(spec.Payload ?? spec.Label);
        }

        if (spec.Action == FullKeyAction.Text)
        {
            return TextVk(spec.Payload ?? spec.Label);
        }

        if (spec.Action == FullKeyAction.Function)
        {
            return NamedVk(spec.Payload ?? spec.Label);
        }

        return spec.Action switch
        {
            FullKeyAction.Backspace => 0x08,
            FullKeyAction.Tab => 0x09,
            FullKeyAction.Enter => 0x0D,
            FullKeyAction.Esc => 0x1B,
            FullKeyAction.Space => 0x20,
            FullKeyAction.Left => 0x25,
            FullKeyAction.Up => 0x26,
            FullKeyAction.Right => 0x27,
            FullKeyAction.Down => 0x28,
            FullKeyAction.Delete => 0x2E,
            _ => null
        };
    }

    public static ushort? LetterVk(string letter)
    {
        if (string.IsNullOrEmpty(letter))
        {
            return null;
        }

        var ch = char.ToUpperInvariant(letter[0]);
        return ch is >= 'A' and <= 'Z' ? ch : null;
    }

    public static ushort? TextVk(string label) => label switch
    {
        "0" => 0x30,
        "1" => 0x31,
        "2" => 0x32,
        "3" => 0x33,
        "4" => 0x34,
        "5" => 0x35,
        "6" => 0x36,
        "7" => 0x37,
        "8" => 0x38,
        "9" => 0x39,
        ";" => 0xBA,
        "=" => 0xBB,
        "," => 0xBC,
        "-" => 0xBD,
        "." => 0xBE,
        "/" => 0xBF,
        "`" => 0xC0,
        "[" => 0xDB,
        "\\" => 0xDC,
        "]" => 0xDD,
        "'" => 0xDE,
        _ => null
    };

    public static ushort? NamedVk(string name) => name switch
    {
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        "Ins" => 0x2D,
        "Home" => 0x24,
        "End" => 0x23,
        "PgUp" => 0x21,
        "PgDn" => 0x22,
        _ => null
    };
}
