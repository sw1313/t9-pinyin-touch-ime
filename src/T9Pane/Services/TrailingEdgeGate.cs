namespace T9Pane.Services;

/// <summary>
/// 事件合并门。合并期间到来的事件必须补跑一轮，不能直接丢弃：
/// 跨进程交接焦点时 UIA 会连发一串事件，只处理第一条会读到尚未更新的位置，
/// 而正确坐标恰恰在后续那几条里。丢掉尾沿就变成“有概率定位错、要多点一下”。
/// </summary>
internal sealed class TrailingEdgeGate
{
    private const int Idle = 0;
    private const int Scheduled = 1;
    private const int Restage = 2;

    private int _state;

    /// <summary>是否需要由调用方安排一轮处理。</summary>
    public bool TryEnter()
    {
        while (true)
        {
            switch (Volatile.Read(ref _state))
            {
                case Idle:
                    if (Interlocked.CompareExchange(ref _state, Scheduled, Idle) == Idle)
                    {
                        return true;
                    }
                    break;
                case Scheduled:
                    // 记下“处理期间又来过事件”，交给 ShouldRerun 补跑。
                    if (Interlocked.CompareExchange(ref _state, Restage, Scheduled)
                        == Scheduled)
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }
    }

    /// <summary>一轮处理结束后调用；返回 true 表示需要再安排一轮。</summary>
    public bool ShouldRerun()
    {
        // 先尝试回到空闲。这一步必须是 CAS：若期间又来过事件（状态已变成
        // Restage），直接写 Idle 会把那条事件连同它携带的新坐标一起丢掉。
        if (Interlocked.CompareExchange(ref _state, Idle, Scheduled) == Scheduled)
        {
            return false;
        }

        Interlocked.Exchange(ref _state, Scheduled);
        return true;
    }
}
