namespace T9Pane.Services;

/// <summary>
/// 按搜狗 / 百度符号表分栏：最近、常用、中英文、表情网络、序号单位、
/// 希腊俄语拉丁、拼音注音、平假片假、部首制表等十几二十类。
/// </summary>
internal static class SymbolCatalog
{
    public const string Recent = "最近";
    public const string Common = "常用";
    public const string Chinese = "中文";
    public const string English = "英文";
    public const string Emoji = "表情";
    public const string Network = "网络";
    public const string Email = "邮箱";
    public const string Serial = "序号";
    public const string Math = "数学";
    public const string Unit = "单位";
    public const string Special = "特殊";
    public const string Greek = "希腊";
    public const string Russian = "俄语";
    public const string Latin = "拉丁";
    public const string Pinyin = "拼音";
    public const string Zhuyin = "注音";
    public const string Hiragana = "平假";
    public const string Katakana = "片假";
    public const string Radical = "部首";
    public const string Box = "制表";

    public static readonly string[] Names =
    [
        Recent, Common, Chinese, English, Emoji, Network, Email,
        Serial, Math, Unit, Special, Greek, Russian, Latin,
        Pinyin, Zhuyin, Hiragana, Katakana, Radical, Box
    ];

    public static string DefaultName(bool english) => english ? English : Chinese;

    public static IReadOnlyList<string> Marks(string name, IReadOnlyList<string> recent) =>
        name switch
        {
            Recent => recent,
            Common => CommonMarks,
            Chinese => ChineseMarks,
            English => EnglishMarks,
            Emoji => EmojiMarks,
            Network => NetworkMarks,
            Email => EmailMarks,
            Serial => SerialMarks,
            Math => MathMarks,
            Unit => UnitMarks,
            Special => SpecialMarks,
            Greek => GreekMarks,
            Russian => RussianMarks,
            Latin => LatinMarks,
            Pinyin => PinyinMarks,
            Zhuyin => ZhuyinMarks,
            Hiragana => HiraganaMarks,
            Katakana => KatakanaMarks,
            Radical => RadicalMarks,
            Box => BoxMarks,
            _ => ChineseMarks
        };

    public static readonly string[] CommonMarks =
    [
        "，", "。", "？", "！", "、", "：", "；", "…",
        ",", ".", "?", "!", ":", ";", "'", "\"",
        "@", "#", "&", "*", "/", "\\", "_", "-",
        "（", "）", "(", ")", "【", "】", "《", "》"
    ];

    public static readonly string[] ChineseMarks =
    [
        "，", "。", "？", "！", "、", "：", "；", "…",
        "“", "”", "‘", "’", "（", "）", "【", "】",
        "《", "》", "〈", "〉", "『", "』", "「", "」",
        "·", "—", "～", "￥", "℃", "°", "※", "§",
        "→", "←", "↑", "↓", "〔", "〕", "〖", "〗"
    ];

    public static readonly string[] EnglishMarks =
    [
        ",", ".", "?", "!", ":", ";", "'", "\"",
        "(", ")", "[", "]", "{", "}", "/", "\\",
        "@", "#", "$", "%", "&", "*", "+", "-",
        "=", "_", "<", ">", "^", "`", "|", "~",
        "€", "£", "¥", "©", "®", "™"
    ];

    public static readonly string[] EmojiMarks =
    [
        "★", "☆", "♥", "♡", "♪", "✓", "✕", "●",
        "○", "■", "□", "▲", "△", "♦", "☀", "☁",
        "☺", "☻", "✨", "→", "←", "↑", "↓", "↔",
        "(^_^)", "(^o^)", "(T_T)", "orz", "→_→", "^_^;",
        "☆彡", "(>_<)", "(^▽^)", "=_="
    ];

    public static readonly string[] NetworkMarks =
    [
        "www.", ".com", ".cn", ".net", ".org", ".edu",
        "http://", "https://", "/", "@", "#", "&",
        "?", "=", "_", "-", "~", ".html",
        ".js", ".css", ".json", "ftp://", ".gov", ".io"
    ];

    public static readonly string[] EmailMarks =
    [
        "@", ".com", ".cn", ".net", "qq.com", "163.com",
        "126.com", "gmail.com", "outlook.com", "hotmail.com",
        "sina.com", "foxmail.com", "139.com", "yeah.net",
        "icloud.com", "_", "-", "."
    ];

    public static readonly string[] SerialMarks =
    [
        "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧",
        "⑨", "⑩", "⑪", "⑫", "⑬", "⑭", "⑮", "⑯",
        "⑰", "⑱", "⑲", "⑳", "⑴", "⑵", "⑶", "⑷",
        "⑸", "⑹", "⑺", "⑻", "⑼", "⑽", "⒈", "⒉",
        "⒊", "⒋", "⒌", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ",
        "Ⅵ", "Ⅶ", "Ⅷ", "Ⅸ", "Ⅹ", "Ⅺ", "Ⅻ",
        "一", "二", "三", "四", "五", "六", "七", "八",
        "九", "十", "壹", "贰", "叁", "肆", "伍", "陆"
    ];

    public static readonly string[] MathMarks =
    [
        "+", "-", "\u00D7", "\u00F7", "=", "\u2260", "\u2248", "\u00B1",
        "<", ">", "\u2264", "\u2265", "%", "\u2030", "\u221A", "\u221E",
        "\u2211", "\u220F", "\u222B", "\u00B0", "\u2032", "\u2033", "\u2234", "\u2235",
        "\u2208", "\u2209", "\u222A", "\u2229", "\u2282", "\u2283", "\u2205", "\u2227",
        "\u2228", "\u00AC", "\u2200", "\u2203", "\u2202", "\u2207", "\u22A5", "\u2220"
    ];

    public static readonly string[] UnitMarks =
    [
        "℃", "℉", "K", "kg", "g", "mg", "t", "lb",
        "m", "km", "cm", "mm", "μm", "nm", "L", "ml",
        "m²", "m³", "Hz", "kHz", "MHz", "Ω", "kΩ", "W",
        "kW", "Pa", "bar", "mol", "cd", "lx", "dB", "rpm"
    ];

    public static readonly string[] SpecialMarks =
    [
        "★", "☆", "●", "○", "■", "□", "▲", "△",
        "▼", "▽", "◆", "◇", "♥", "♡", "♠", "♣",
        "♪", "♫", "✓", "✕", "※", "§", "¶", "†",
        "♀", "♂", "☀", "☁", "☺", "☹", "©", "®",
        "™", "℃", "°", "∞", "→", "←", "↑", "↓",
        "↔", "⇒", "⇐", "⇔", "☑", "☐", "☒", "✔"
    ];

    public static readonly string[] GreekMarks =
    [
        "Α", "Β", "Γ", "Δ", "Ε", "Ζ", "Η", "Θ",
        "Ι", "Κ", "Λ", "Μ", "Ν", "Ξ", "Ο", "Π",
        "Ρ", "Σ", "Τ", "Υ", "Φ", "Χ", "Ψ", "Ω",
        "α", "β", "γ", "δ", "ε", "ζ", "η", "θ",
        "ι", "κ", "λ", "μ", "ν", "ξ", "ο", "π",
        "ρ", "σ", "τ", "υ", "φ", "χ", "ψ", "ω"
    ];

    public static readonly string[] RussianMarks =
    [
        "А", "Б", "В", "Г", "Д", "Е", "Ё", "Ж",
        "З", "И", "Й", "К", "Л", "М", "Н", "О",
        "П", "Р", "С", "Т", "У", "Ф", "Х", "Ц",
        "Ч", "Ш", "Щ", "Ъ", "Ы", "Ь", "Э", "Ю",
        "Я", "а", "б", "в", "г", "д", "е", "ё",
        "ж", "з", "и", "й", "к", "л", "м", "н",
        "о", "п", "р", "с", "т", "у", "ф", "х",
        "ц", "ч", "ш", "щ", "ъ", "ы", "ь", "э",
        "ю", "я"
    ];

    public static readonly string[] LatinMarks =
    [
        "À", "Á", "Â", "Ã", "Ä", "Å", "Æ", "Ç",
        "È", "É", "Ê", "Ë", "Ì", "Í", "Î", "Ï",
        "Ñ", "Ò", "Ó", "Ô", "Õ", "Ö", "Ø", "Ù",
        "Ú", "Û", "Ü", "Ý", "Þ", "ß", "à", "á",
        "â", "ã", "ä", "å", "æ", "ç", "è", "é",
        "ê", "ë", "ì", "í", "î", "ï", "ñ", "ò",
        "ó", "ô", "õ", "ö", "ø", "ù", "ú", "û",
        "ü", "ý", "þ", "ÿ"
    ];

    public static readonly string[] PinyinMarks =
    [
        "ā", "á", "ǎ", "à", "ō", "ó", "ǒ", "ò",
        "ē", "é", "ě", "è", "ī", "í", "ǐ", "ì",
        "ū", "ú", "ǔ", "ù", "ǖ", "ǘ", "ǚ", "ǜ",
        "ü", "ń", "ň", "ê", "˙"
    ];

    public static readonly string[] ZhuyinMarks =
    [
        "\u3105", "\u3106", "\u3107", "\u3108", "\u3109", "\u310A", "\u310B", "\u310C",
        "\u310D", "\u310E", "\u310F", "\u3110", "\u3111", "\u3112", "\u3113", "\u3114",
        "\u3115", "\u3116", "\u3117", "\u3118", "\u3119", "\u311A", "\u311B", "\u311C",
        "\u311D", "\u311E", "\u311F", "\u3120", "\u3121", "\u3122", "\u3123", "\u3124",
        "\u3125", "\u3126", "\u3127", "\u3128", "\u3129"
    ];

    public static readonly string[] HiraganaMarks =
    [
        "あ", "い", "う", "え", "お", "か", "き", "く",
        "け", "こ", "さ", "し", "す", "せ", "そ", "た",
        "ち", "つ", "て", "と", "な", "に", "ぬ", "ね",
        "の", "は", "ひ", "ふ", "へ", "ほ", "ま", "み",
        "む", "め", "も", "や", "ゆ", "よ", "ら", "り",
        "る", "れ", "ろ", "わ", "を", "ん", "が", "ぎ",
        "ぐ", "げ", "ご", "ざ", "じ", "ず", "ぜ", "ぞ",
        "だ", "ぢ", "づ", "で", "ど", "ば", "び", "ぶ",
        "べ", "ぼ", "ぱ", "ぴ", "ぷ", "ぺ", "ぽ", "ぁ",
        "ぃ", "ぅ", "ぇ", "ぉ", "っ", "ゃ", "ゅ", "ょ",
        "ー"
    ];

    public static readonly string[] KatakanaMarks =
    [
        "ア", "イ", "ウ", "エ", "オ", "カ", "キ", "ク",
        "ケ", "コ", "サ", "シ", "ス", "セ", "ソ", "タ",
        "チ", "ツ", "テ", "ト", "ナ", "ニ", "ヌ", "ネ",
        "ノ", "ハ", "ヒ", "フ", "ヘ", "ホ", "マ", "ミ",
        "ム", "メ", "モ", "ヤ", "ユ", "ヨ", "ラ", "リ",
        "ル", "レ", "ロ", "ワ", "ヲ", "ン", "ガ", "ギ",
        "グ", "ゲ", "ゴ", "ザ", "ジ", "ズ", "ゼ", "ゾ",
        "ダ", "ヂ", "ヅ", "デ", "ド", "バ", "ビ", "ブ",
        "ベ", "ボ", "パ", "ピ", "プ", "ペ", "ポ", "ァ",
        "ィ", "ゥ", "ェ", "ォ", "ッ", "ャ", "ュ", "ョ",
        "ー"
    ];

    public static readonly string[] RadicalMarks =
    [
        "丨", "亅", "丿", "乛", "丶", "一", "乙", "乚",
        "亻", "彳", "讠", "饣", "扌", "氵", "冫", "忄",
        "丬", "犭", "\u7eab", "艹", "屮", "钅", "礻", "衤",
        "⺮", "⻊", "辶", "⻌", "囗", "冂", "冖", "宀",
        "广", "疒", "阝", "刂", "卩", "厂"
    ];

    public static readonly string[] BoxMarks =
    [
        "─", "│", "┌", "┐", "└", "┘", "├", "┤",
        "┬", "┴", "┼", "═", "║", "╔", "╗", "╚",
        "╝", "╠", "╣", "╦", "╩", "╬", "╭", "╮",
        "╰", "╯", "╱", "╲", "╳", "╴", "╵", "╶"
    ];
}
