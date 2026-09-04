#pragma once
#include <windows.h>
#include <msctf.h>
#include <ctffunc.h>
#include <string>

class T9Ime : public ITfTextInputProcessorEx,
              public ITfThreadMgrEventSink,
              public ITfThreadFocusSink,
              public ITfTextEditSink,
              public ITfTextLayoutSink,
              public ITfKeyEventSink,
              public ITfInputProcessorProfileActivationSink,
              public ITfCompositionSink,
              public ITfFunctionProvider,
              public ITfFnGetPreferredTouchKeyboardLayout
{
public:
    T9Ime();
    virtual ~T9Ime();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv);
    STDMETHODIMP_(ULONG) AddRef();
    STDMETHODIMP_(ULONG) Release();

    // ITfTextInputProcessor
    STDMETHODIMP Activate(ITfThreadMgr* ptim, TfClientId tid);
    STDMETHODIMP Deactivate();

    // ITfTextInputProcessorEx
    STDMETHODIMP ActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags);

    // ITfThreadMgrEventSink
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr*);
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr*);
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* pDoc, ITfDocumentMgr*);
    STDMETHODIMP OnPushContext(ITfContext*);
    STDMETHODIMP OnPopContext(ITfContext*);

    // ITfThreadFocusSink
    STDMETHODIMP OnSetThreadFocus();
    STDMETHODIMP OnKillThreadFocus();

    // ITfTextEditSink
    STDMETHODIMP OnEndEdit(ITfContext* context, TfEditCookie readCookie, ITfEditRecord* record);

    // ITfTextLayoutSink
    STDMETHODIMP OnLayoutChange(ITfContext* context, TfLayoutCode code, ITfContextView* view);

    // ITfKeyEventSink
    STDMETHODIMP OnSetFocus(BOOL fForeground);
    STDMETHODIMP OnTestKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten);
    STDMETHODIMP OnTestKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten);
    STDMETHODIMP OnKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten);
    STDMETHODIMP OnKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten);
    STDMETHODIMP OnPreservedKey(ITfContext* pic, REFGUID rguid, BOOL* pfEaten);

    // ITfInputProcessorProfileActivationSink
    STDMETHODIMP OnActivated(DWORD profileType, LANGID langid, REFCLSID clsid,
        REFGUID category, REFGUID profile, HKL layout, DWORD flags);

    // ITfCompositionSink
    STDMETHODIMP OnCompositionTerminated(TfEditCookie ecWrite, ITfComposition* pComposition);

    // ITfFunctionProvider — SearchHost / TabTip 通过 AdviseSingleSink 查询。
    // https://learn.microsoft.com/en-us/windows/win32/api/msctf/nn-msctf-itffunctionprovider
    STDMETHODIMP GetType(GUID* pguid);
    STDMETHODIMP GetDescription(BSTR* pbstrDesc);
    STDMETHODIMP GetFunction(REFGUID rguid, REFIID riid, IUnknown** ppunk);

    // ITfFunction / ITfFnGetPreferredTouchKeyboardLayout
    // https://learn.microsoft.com/en-us/windows/win32/api/ctffunc/nn-ctffunc-itffngetpreferredtouchkeyboardlayout
    STDMETHODIMP GetDisplayName(BSTR* pbstrName);
    STDMETHODIMP GetLayout(TKBLayoutType* ptkblayoutType, WORD* pwPreferredLayoutId);

    void ApplyText(const wchar_t* text, int kind);
    void HandleLift(const wchar_t* spec);
    void HandleFrame(const void* data, int bytes);
    void QueueApply(int kind, const wchar_t* text);
    void QueuePacket(int kind, const void* data, int bytes);
    bool IsCurrentContext(ITfContext* context);
    void PostReturnKey();

private:
    struct ContextGeometry
    {
        RECT Caret = {};
        RECT Screen = {};
        HWND ViewWindow = nullptr;
        bool HasCaret = false;
        bool HasScreen = false;
        bool LayoutPending = false;
        bool HasRangeSelection = false;
    };

    class ContextProbeSession;

    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
    static DWORD WINAPI CmdPipeThread(PVOID param);
    bool EnsureMessageWindow();
    void StartCmdPipe();
    void StopCmdPipe();
    void NotifyBackend(const char* json);
    bool EnsureBackend();
    HRESULT RequestInsert(const wchar_t* text, int kind);
    bool IsEditableDocument(ITfDocumentMgr* document);
    bool IsEditableContext(ITfContext* context);
    void BindFocusedContext(ITfDocumentMgr* document);
    void BindActiveView();
    void UnbindFocusedContext();
    void RefreshDocumentState();
    void RefreshFromKeyContext(ITfContext* context);
    void PublishDocumentState(ITfDocumentMgr* document);
    void PublishContextState(
        ITfContext* context,
        TfEditCookie readCookie = TF_INVALID_EDIT_COOKIE,
        int source = 0);
    void CompleteContextProbe(
        ITfContext* context,
        LONG epoch,
        const ContextGeometry& geometry,
        int source = 0);
    void EmitContextState(
        bool active,
        LONG epoch,
        const ContextGeometry& geometry,
        int source = 0);
    void PublishThreadState(bool focused);
    void AdviseFunctionProvider();
    void UnadviseFunctionProvider();

    LONG _ref;
    ITfThreadMgr* _threadMgr;
    TfClientId _clientId;
    DWORD _threadCookie;
    DWORD _threadFocusCookie;
    DWORD _keyCookie;
    DWORD _profileCookie;
    HWND _msgHwnd;
    volatile LONG _stateSequence;
    volatile LONG _profileActive;
    volatile LONG _documentFocused;
    volatile LONG _foregroundFocused;
    volatile LONG _contextEpoch;
    volatile LONG _activeFlags;
    ITfContext* _activeContext;
    ITfContextView* _activeView;
    HWND _activeViewWindow;
    DWORD _textEditCookie;
    DWORD _textLayoutCookie;
    HWND _bandHost;
    HWND _bandOwner;
    HBITMAP _bandBitmap;
    DWORD _bandHostBand;
    bool _bandChild;
    bool _bandVisible;
    POINT _bandPointerDown;
    bool _bandPointerActive;
    bool _bandDragging;
    POINT _bandDragCursor;
    POINT _bandDragWindow;
    int _bandFrameWidth;
    int _bandFrameHeight;
    int _bandX;
    int _bandY;
    UINT32 _bandPointerId;
    static LRESULT CALLBACK BandProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
    POINT MapBandPointer(HWND hwnd, POINT client) const;
    bool EnsureBandHost();
    void HideBandHost();
    bool BlitFrame(int x, int y, int width, int height, const BYTE* pixels);
    HWND FindHostOwner();
    DWORD FindHostBand();
    ITfComposition* _composition;
    LONG _lastComposeLen;
    HANDLE _cmdStop;
    HANDLE _cmdThread;
    HANDLE _cmdClient;
    ITfFnSearchCandidateProvider* _searchCandidates;
    bool _functionProviderAdvised;
};

enum T9ImeKind
{
    T9KindCompose = 1,
    T9KindCommit = 2,
    T9KindCancel = 3,
    T9KindBackspace = 4,
    T9KindLift = 5,
    T9KindFrame = 6,
    T9KindQueryState = 7,
    T9KindReturn = 8,
    T9KindSearchCandidates = 9
};

HRESULT RegisterT9Ime();
HRESULT UnregisterT9Ime();
void LaunchBackend();
STDAPI T9ImeClearDefault();
