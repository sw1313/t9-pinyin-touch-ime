using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 光标采样的质量闸门。
///
/// 同一个输入框的探测结果并不稳定：Chromium 的无障碍缓存会不时在渲染进程里重建
/// （官方文档称耗时不到一秒），期间 TextPattern 拿不到光标，只能退到元素外框。
/// 外框锚在框的顶边，真实光标却在框内某一行上，两者可以差几十上百像素——
/// 键盘就在这两个位置之间来回跳，按外框摆放时正好压住真实的输入行。
///
/// 所以质量下降的样本不能直接采用：短时间内继续用上一次更可靠的坐标，
/// 等好样本回来。这里只压制"变差"，不接管"彻底读不到"——那属于焦点真的走了，
/// 必须照常隐藏，否则会变成失焦后键盘赖在原地不走。
/// </summary>
internal sealed class CaretQualityGate
{
    /// <summary>好样本恢复通常在几百毫秒内；再长就宁可用差坐标，也不能长期停在旧位置。</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// 坐标可靠度。TSF 的 GetTextExt 与 UIA 的 TextPattern 都直接描述插入点；
    /// "点中的框"是用户这一下实际点的输入框，比任意元素的外框可靠，
    /// 但毕竟只是框而不是插入点。
    /// </summary>
    public static int Rank(string source) => source switch
    {
        "caret" or "uia/text" => 3,
        "clicked" => 2,
        "uia/box" => 1,
        _ => 0,
    };

    public static bool PrefersCached(
        int cachedRank,
        int freshRank,
        bool sameField,
        long cachedTicks,
        long nowTicks,
        TimeSpan hold) =>
        sameField
        && cachedRank > freshRank
        && cachedTicks != 0
        && nowTicks >= cachedTicks
        && nowTicks - cachedTicks <= hold.Ticks;

    /// <summary>
    /// SearchHost 的 uia/text 与 uia/box 元素身份不同，但描述的是同一个搜索框。
    /// 外框锚在左上角 (72,100)，插入点在 (134,107)，当成换框就会把键盘拽飞。
    /// 开始菜单与任务栏搜索的 Y 差近千像素，不会被这条当成同一个框。
    /// </summary>
    public static bool IsSameSearchChrome(
        bool sameSurface,
        int cachedRank,
        int freshRank,
        NativeRect cachedCaret,
        NativeRect freshCaret) =>
        sameSurface
        && cachedRank >= 3
        && freshRank <= 1
        && !cachedCaret.IsEmpty
        && !freshCaret.IsEmpty
        && Math.Abs(freshCaret.Top - cachedCaret.Top) <= 80
        && Math.Abs(freshCaret.Left - cachedCaret.Left) <= 160;

    private IntPtr _surface;
    private string _fieldId = string.Empty;
    private NativeRect _caret;
    private string _source = string.Empty;
    private long _stampTicks;

    /// <summary>
    /// 用上一次的好坐标替换这次变差的坐标；返回 true 表示发生了替换。
    /// </summary>
    public bool Apply(IntPtr surface, string fieldId, ref NativeRect caret, ref string source)
    {
        var now = DateTime.UtcNow.Ticks;
        // 判定"同一个框"必须连元素身份一起比。只比窗口句柄的话，两个系统搜索框
        // 互切时窗口没变，会被认成同一个框在变差，于是继续沿用上一个框的坐标。
        var sameField = surface == _surface
            && string.Equals(fieldId, _fieldId, StringComparison.Ordinal);
        var sameSearchChrome = IsSameSearchChrome(
            surface == _surface,
            Rank(_source),
            Rank(source),
            _caret,
            caret);
        if (PrefersCached(
                Rank(_source),
                Rank(source),
                sameField || sameSearchChrome,
                _stampTicks,
                now,
                Hold))
        {
            caret = _caret;
            source = _source;
            return true;
        }

        _surface = surface;
        _fieldId = fieldId;
        _caret = caret;
        _source = source;
        _stampTicks = now;
        return false;
    }
}
