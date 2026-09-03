using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 官方 ITfInputProcessorProfileActivationSink，挂在 ITfThreadMgr 上。
/// 本机 CoCreate CLSID_TF_ThreadMgr 会 CLASSNOTREG，必须用 msctf.dll 的 TF_CreateThreadMgr。
/// 这条线程 Activate 之后，GetActiveProfile 只代表本线程；读语言栏请走 TsfLayoutSelection 的独立 STA。
/// </summary>
internal sealed class TsfProfileActivationSink : ITfInputProcessorProfileActivationSink, IDisposable
{
    private static readonly Guid IidProfileSink = new("71C6E74E-0F28-11D8-A82A-00065B84435C");
    private static readonly Guid TipKeyboard = OfficialT9ProfilePolicy.TipKeyboard;

    private readonly Action<bool> _onT9;
    private ITfThreadMgr? _threadMgr;
    private ITfSource? _source;
    private uint _cookie = uint.MaxValue;

    public TsfProfileActivationSink(Action<bool> onT9) => _onT9 = onT9;

    public void Start()
    {
        // 本进程一旦 Activate ITfThreadMgr，同进程 GetActiveProfile 就不再跟语言栏，
        // 会读成 T9Pane 自己这条线程（启动时是微软拼音，之后再也跟不上）。
        // 语言栏切换改由 IME 通知和 EVENT_OBJECT_IME_* 触发，再读官方 GetActiveProfile。
    }

    public int OnActivated(
        uint profileType,
        ushort langid,
        ref Guid clsid,
        ref Guid catid,
        ref Guid guidProfile,
        IntPtr hkl,
        uint flags)
    {
        if (catid != TipKeyboard && catid != Guid.Empty)
        {
            return 0;
        }

        _ = profileType;
        _ = langid;
        _ = hkl;
        _onT9(OfficialT9ProfilePolicy.IsT9Layout(clsid, guidProfile) && (flags & 1) != 0);
        return 0;
    }

    public void Dispose() => Release();

    private void Release()
    {
        try
        {
            if (_source is not null && _cookie != uint.MaxValue)
            {
                _source.UnadviseSink(_cookie);
            }
        }
        catch
        {
            // 退出时 TSF 可能已经拆掉
        }

        _cookie = uint.MaxValue;
        if (_threadMgr is not null)
        {
            try
            {
                _threadMgr.Deactivate();
            }
            catch
            {
                // 忽略
            }
        }

        if (_source is not null && Marshal.IsComObject(_source))
        {
            Marshal.ReleaseComObject(_source);
        }

        if (_threadMgr is not null && Marshal.IsComObject(_threadMgr))
        {
            Marshal.ReleaseComObject(_threadMgr);
        }

        _source = null;
        _threadMgr = null;
    }
}

[ComImport]
[Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    [PreserveSig]
    int Activate(out uint clientId);

    [PreserveSig]
    int Deactivate();
}

[ComImport]
[Guid("4EA48A35-60AE-446F-8FD6-E6A8D82459F7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfSource
{
    [PreserveSig]
    int AdviseSink(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] object sink, out uint cookie);

    [PreserveSig]
    int UnadviseSink(uint cookie);
}

[ComImport]
[Guid("71C6E74E-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileActivationSink
{
    [PreserveSig]
    int OnActivated(
        uint profileType,
        ushort langid,
        ref Guid clsid,
        ref Guid catid,
        ref Guid guidProfile,
        IntPtr hkl,
        uint flags);
}
