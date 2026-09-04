#include "T9Ime.h"
#include "Guids.h"
#include "SipCancel.h"
#include "TsfFunctions.h"
#include <msctf.h>
#include <oleauto.h>
#include <sddl.h>
#include <climits>
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <new>

extern HINSTANCE g_hInst;

#ifndef WM_TABLET_QUERYSYSTEMGESTURESTATUS
#define WM_TABLET_DEFBASE 0x02C0
#define WM_TABLET_QUERYSYSTEMGESTURESTATUS (WM_TABLET_DEFBASE + 12)
#endif
#ifndef TABLET_DISABLE_PRESSANDHOLD
#define TABLET_DISABLE_PRESSANDHOLD 0x00000001
#define TABLET_DISABLE_PENTAPFEEDBACK 0x00000008
#define TABLET_DISABLE_PENBARRELFEEDBACK 0x00000010
#define TABLET_DISABLE_FLICKS 0x00010000
#endif

namespace
{
    const UINT WmT9Apply = WM_APP + 21;

    void DisablePressAndHold(HWND hwnd)
    {
        if (!hwnd)
        {
            return;
        }

        GESTURECONFIG config = {};
        config.dwID = 0;
        config.dwWant = 0;
        config.dwBlock = GC_ALLGESTURES;
        SetGestureConfig(hwnd, 0, 1, &config, sizeof(config));
        SetPropW(
            hwnd,
            L"MicrosoftTabletPenServiceProperty",
            reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(
                TABLET_DISABLE_PRESSANDHOLD |
                TABLET_DISABLE_PENTAPFEEDBACK |
                TABLET_DISABLE_PENBARRELFEEDBACK |
                TABLET_DISABLE_FLICKS)));
    }

    // SampleIME _IsKeyEaten：不组合就不拦截。T9 用屏幕键盘上屏，
    // 实体键必须原样交给应用，否则回车/Ctrl+Space 切输入法都会被吞掉。

    HWND ResolveViewWindow(ITfContextView* view)
    {
        HWND window = nullptr;
        if (!view || FAILED(view->GetWnd(&window)) || !window)
        {
            window = GetFocus();
        }
        return window;
    }

    // EasyIME / Chromium：TYPE_NONE 文档会置 KEYBOARD_DISABLED 与 EMPTYCONTEXT，
    // 并不一定带 TF_SD_READONLY。只看只读位会把“离开输入框”当成还在编辑。
    bool ContextCompartmentIsSet(ITfContext* context, REFGUID compartmentId)
    {
        ITfCompartmentMgr* manager = nullptr;
        if (!context
            || FAILED(context->QueryInterface(
                IID_ITfCompartmentMgr,
                reinterpret_cast<void**>(&manager)))
            || !manager)
        {
            return false;
        }

        ITfCompartment* compartment = nullptr;
        auto set = false;
        if (SUCCEEDED(manager->GetCompartment(compartmentId, &compartment))
            && compartment)
        {
            VARIANT value;
            VariantInit(&value);
            if (SUCCEEDED(compartment->GetValue(&value))
                && value.vt == VT_I4
                && value.lVal != 0)
            {
                set = true;
            }
            VariantClear(&value);
            compartment->Release();
        }

        manager->Release();
        return set;
    }

    class EditSession : public ITfEditSession
    {
    public:
        EditSession(ITfContext* ctx, T9Ime* ime, const wchar_t* text, int kind, ITfComposition** slot, LONG* lastLen)
            : _ref(1), _ctx(ctx), _ime(ime), _text(text ? text : L""), _kind(kind), _slot(slot), _lastLen(lastLen)
        {
            if (_ctx)
            {
                _ctx->AddRef();
            }
        }

        ~EditSession()
        {
            if (_ctx)
            {
                _ctx->Release();
            }
        }

        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            if (riid == IID_IUnknown || riid == IID_ITfEditSession)
            {
                *ppv = static_cast<ITfEditSession*>(this);
                AddRef();
                return S_OK;
            }
            *ppv = nullptr;
            return E_NOINTERFACE;
        }

        STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_ref); }
        STDMETHODIMP_(ULONG) Release()
        {
            const auto n = InterlockedDecrement(&_ref);
            if (n == 0)
            {
                delete this;
            }
            return n;
        }

        STDMETHODIMP DoEditSession(TfEditCookie ec)
        {
            if (!_ctx || !_ime || !_ime->IsCurrentContext(_ctx))
            {
                return E_ABORT;
            }

            if (_kind == T9KindCancel)
            {
                if (_slot && *_slot)
                {
                    EndComp(ec);
                    return S_OK;
                }

                DeleteChars(ec, LastLen());
                SetLastLen(0);
                return S_OK;
            }

            if (_kind == T9KindBackspace)
            {
                if (_slot && *_slot)
                {
                    EndComp(ec);
                    return S_OK;
                }

                if (LastLen() > 0)
                {
                    DeleteChars(ec, LastLen());
                    SetLastLen(0);
                    return S_OK;
                }

                DeleteChars(ec, 1);
                return S_OK;
            }

            if (_kind == T9KindCommit)
            {
                if (_slot && *_slot)
                {
                    EndComp(ec);
                }
                else if (LastLen() > 0)
                {
                    DeleteChars(ec, LastLen());
                    SetLastLen(0);
                }

                ITfInsertAtSelection* insert = nullptr;
                if (FAILED(_ctx->QueryInterface(IID_ITfInsertAtSelection, reinterpret_cast<void**>(&insert))) || !insert)
                {
                    return E_FAIL;
                }

                ITfRange* range = nullptr;
                insert->InsertTextAtSelection(ec, 0, _text.c_str(), static_cast<LONG>(_text.size()), &range);
                insert->Release();
                if (range)
                {
                    range->Collapse(ec, TF_ANCHOR_END);
                    TF_SELECTION sel = {};
                    sel.range = range;
                    sel.style.ase = TF_AE_NONE;
                    sel.style.fInterimChar = FALSE;
                    _ctx->SetSelection(ec, 1, &sel);
                    range->Release();
                }
                return S_OK;
            }

            ITfInsertAtSelection* insert = nullptr;
            if (FAILED(_ctx->QueryInterface(IID_ITfInsertAtSelection, reinterpret_cast<void**>(&insert))) || !insert)
            {
                return E_FAIL;
            }

            ITfRange* range = nullptr;
            if (_slot && *_slot)
            {
                (*_slot)->GetRange(&range);
            }
            else if (LastLen() > 0)
            {
                range = RangeBeforeCaret(ec, LastLen());
            }

            if (!range)
            {
                insert->InsertTextAtSelection(ec, TF_IAS_QUERYONLY, nullptr, 0, &range);
            }

            if (range && !(_slot && *_slot))
            {
                ITfContextComposition* cc = nullptr;
                if (SUCCEEDED(_ctx->QueryInterface(IID_ITfContextComposition, reinterpret_cast<void**>(&cc))) && cc)
                {
                    cc->StartComposition(ec, range, _ime, _slot);
                    cc->Release();
                }
            }
            insert->Release();

            if (range)
            {
                range->SetText(ec, 0, _text.c_str(), static_cast<LONG>(_text.size()));
                ITfRange* caret = nullptr;
                if (SUCCEEDED(range->Clone(&caret)) && caret)
                {
                    caret->Collapse(ec, TF_ANCHOR_END);
                    TF_SELECTION sel = {};
                    sel.range = caret;
                    sel.style.ase = TF_AE_NONE;
                    sel.style.fInterimChar = FALSE;
                    _ctx->SetSelection(ec, 1, &sel);
                    caret->Release();
                }
                range->Release();
            }

            SetLastLen(static_cast<LONG>(_text.size()));
            return S_OK;
        }

    private:
        void EndComp(TfEditCookie ec)
        {
            if (_slot && *_slot)
            {
                ITfRange* range = nullptr;
                (*_slot)->GetRange(&range);
                if (range)
                {
                    range->SetText(ec, 0, L"", 0);
                    range->Release();
                }
                (*_slot)->EndComposition(ec);
                (*_slot)->Release();
                *_slot = nullptr;
            }

            SetLastLen(0);
        }

        void DeleteChars(TfEditCookie ec, LONG count)
        {
            auto* range = RangeBeforeCaret(ec, count);
            if (!range)
            {
                return;
            }

            range->SetText(ec, 0, L"", 0);
            range->Release();
        }

        ITfRange* RangeBeforeCaret(TfEditCookie ec, LONG count)
        {
            if (count <= 0)
            {
                return nullptr;
            }

            TF_SELECTION sel = {};
            ULONG fetched = 0;
            if (FAILED(_ctx->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &sel, &fetched)) || fetched == 0 || !sel.range)
            {
                return nullptr;
            }

            BOOL empty = TRUE;
            if (SUCCEEDED(sel.range->IsEmpty(ec, &empty)) && !empty)
            {
                return sel.range;
            }

            sel.range->Collapse(ec, TF_ANCHOR_END);
            LONG moved = 0;
            sel.range->ShiftStart(ec, -count, &moved, nullptr);
            if (moved == 0)
            {
                sel.range->Release();
                return nullptr;
            }

            return sel.range;
        }

        LONG LastLen() const
        {
            return _lastLen ? *_lastLen : 0;
        }

        void SetLastLen(LONG value)
        {
            if (_lastLen)
            {
                *_lastLen = value;
            }
        }

        LONG _ref;
        ITfContext* _ctx;
        T9Ime* _ime;
        std::wstring _text;
        int _kind;
        ITfComposition** _slot;
        LONG* _lastLen;
    };
}

class T9Ime::ContextProbeSession : public ITfEditSession
{
public:
    ContextProbeSession(ITfContext* context, T9Ime* ime, LONG epoch, int source)
        : _ref(1), _context(context), _ime(ime), _epoch(epoch), _source(source)
    {
        if (_context)
        {
            _context->AddRef();
        }
        if (_ime)
        {
            _ime->AddRef();
        }
    }

    ~ContextProbeSession()
    {
        if (_context)
        {
            _context->Release();
        }
        if (_ime)
        {
            _ime->Release();
        }
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv)
        {
            return E_INVALIDARG;
        }
        if (riid == IID_IUnknown || riid == IID_ITfEditSession)
        {
            *ppv = static_cast<ITfEditSession*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_ref); }
    STDMETHODIMP_(ULONG) Release()
    {
        const auto value = InterlockedDecrement(&_ref);
        if (value == 0)
        {
            delete this;
        }
        return value;
    }

    STDMETHODIMP DoEditSession(TfEditCookie ec)
    {
        if (!_context || !_ime)
        {
            return E_FAIL;
        }
        if (_context != _ime->_activeContext
            || _epoch != InterlockedCompareExchange(&_ime->_contextEpoch, 0, 0))
        {
            return E_ABORT;
        }

        ContextGeometry geometry = {};
        ITfContextView* view = nullptr;
        if (SUCCEEDED(_context->GetActiveView(&view)) && view)
        {
            geometry.ViewWindow = ResolveViewWindow(view);
            geometry.HasScreen = SUCCEEDED(view->GetScreenExt(&geometry.Screen))
                && geometry.Screen.right > geometry.Screen.left
                && geometry.Screen.bottom > geometry.Screen.top;

            TF_SELECTION selection = {};
            ULONG fetched = 0;
            if (SUCCEEDED(_context->GetSelection(
                ec,
                TF_DEFAULT_SELECTION,
                1,
                &selection,
                &fetched))
                && fetched > 0
                && selection.range)
            {
                BOOL emptyRange = TRUE;
                if (SUCCEEDED(selection.range->IsEmpty(ec, &emptyRange)))
                {
                    geometry.HasRangeSelection = emptyRange == FALSE;
                }

                ITfRange* caret = nullptr;
                if (SUCCEEDED(selection.range->Clone(&caret)) && caret)
                {
                    caret->Collapse(
                        ec,
                        selection.style.ase == TF_AE_START
                            ? TF_ANCHOR_START
                            : TF_ANCHOR_END);
                    BOOL clipped = FALSE;
                    const auto extentResult = view->GetTextExt(
                        ec,
                        caret,
                        &geometry.Caret,
                        &clipped);
                    geometry.LayoutPending = extentResult == TS_E_NOLAYOUT;
                    geometry.HasCaret = SUCCEEDED(extentResult)
                        && geometry.Caret.bottom > geometry.Caret.top;
                    if (geometry.HasCaret
                        && geometry.Caret.right <= geometry.Caret.left)
                    {
                        geometry.Caret.right = geometry.Caret.left + 2;
                    }
                    caret->Release();
                }
                selection.range->Release();
            }

            view->Release();
        }

        _ime->CompleteContextProbe(_context, _epoch, geometry, _source);
        return S_OK;
    }

private:
    LONG _ref;
    ITfContext* _context;
    T9Ime* _ime;
    LONG _epoch;
    int _source;
};

T9Ime::T9Ime()
    : _ref(1), _threadMgr(nullptr), _clientId(TF_CLIENTID_NULL),
      _threadCookie(TF_INVALID_COOKIE), _threadFocusCookie(TF_INVALID_COOKIE),
      _keyCookie(TF_INVALID_COOKIE),
      _profileCookie(TF_INVALID_COOKIE),
      _msgHwnd(nullptr), _stateSequence(0), _profileActive(0),
      _documentFocused(0), _foregroundFocused(0),
      _contextEpoch(0), _activeFlags(0),
      _activeContext(nullptr), _activeView(nullptr), _activeViewWindow(nullptr),
      _textEditCookie(TF_INVALID_COOKIE), _textLayoutCookie(TF_INVALID_COOKIE),
      _bandHost(nullptr), _bandOwner(nullptr), _bandBitmap(nullptr), _bandHostBand(0),
      _bandChild(false), _bandVisible(false),
      _bandPointerDown({ 0, 0 }), _bandPointerActive(false),
      _bandDragging(false), _bandDragCursor({ 0, 0 }), _bandDragWindow({ 0, 0 }),
      _bandFrameWidth(0), _bandFrameHeight(0), _bandX(INT_MIN), _bandY(INT_MIN),
      _bandPointerId(0),
      _composition(nullptr), _lastComposeLen(0),
      _cmdStop(nullptr), _cmdThread(nullptr), _cmdClient(nullptr),
      _searchCandidates(nullptr), _functionProviderAdvised(false)
{
}

T9Ime::~T9Ime()
{
    Deactivate();
}

STDMETHODIMP T9Ime::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv)
    {
        return E_INVALIDARG;
    }

    if (riid == IID_IUnknown || riid == IID_ITfTextInputProcessor || riid == IID_ITfTextInputProcessorEx)
    {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    }
    else if (riid == IID_ITfThreadMgrEventSink)
    {
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    }
    else if (riid == IID_ITfThreadFocusSink)
    {
        *ppv = static_cast<ITfThreadFocusSink*>(this);
    }
    else if (riid == IID_ITfTextEditSink)
    {
        *ppv = static_cast<ITfTextEditSink*>(this);
    }
    else if (riid == IID_ITfTextLayoutSink)
    {
        *ppv = static_cast<ITfTextLayoutSink*>(this);
    }
    else if (riid == IID_ITfKeyEventSink)
    {
        *ppv = static_cast<ITfKeyEventSink*>(this);
    }
    else if (riid == IID_ITfInputProcessorProfileActivationSink)
    {
        *ppv = static_cast<ITfInputProcessorProfileActivationSink*>(this);
    }
    else if (riid == IID_ITfCompositionSink)
    {
        *ppv = static_cast<ITfCompositionSink*>(this);
    }
    else if (riid == IID_ITfFunctionProvider)
    {
        *ppv = static_cast<ITfFunctionProvider*>(this);
    }
    else if (riid == IID_ITfFunction || riid == IID_ITfFnGetPreferredTouchKeyboardLayout)
    {
        *ppv = static_cast<ITfFnGetPreferredTouchKeyboardLayout*>(this);
    }
    else
    {
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) T9Ime::AddRef() { return InterlockedIncrement(&_ref); }
STDMETHODIMP_(ULONG) T9Ime::Release()
{
    const auto n = InterlockedDecrement(&_ref);
    if (n == 0)
    {
        delete this;
    }
    return n;
}

STDMETHODIMP T9Ime::Activate(ITfThreadMgr* ptim, TfClientId tid)
{
    return ActivateEx(ptim, tid, 0);
}

STDMETHODIMP T9Ime::ActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags)
{
    if (!ptim)
    {
        return E_INVALIDARG;
    }

    _threadMgr = ptim;
    _threadMgr->AddRef();
    _clientId = tid;
    DWORD activeFlags = dwFlags;
    ITfThreadMgrEx* threadManagerEx = nullptr;
    if (SUCCEEDED(_threadMgr->QueryInterface(
            IID_ITfThreadMgrEx,
            reinterpret_cast<void**>(&threadManagerEx)))
        && threadManagerEx)
    {
        DWORD liveFlags = 0;
        if (SUCCEEDED(threadManagerEx->GetActiveFlags(&liveFlags)) && liveFlags != 0)
        {
            activeFlags = liveFlags;
        }
        threadManagerEx->Release();
    }
    InterlockedExchange(&_activeFlags, static_cast<LONG>(activeFlags));
    InterlockedExchange(&_profileActive, 1);
    ITfDocumentMgr* focusedDocument = nullptr;
    if (SUCCEEDED(_threadMgr->GetFocus(&focusedDocument)) && focusedDocument)
    {
        BindFocusedContext(focusedDocument);
        InterlockedExchange(
            &_documentFocused,
            IsEditableContext(_activeContext) ? 1 : 0);
        focusedDocument->Release();
    }
    BOOL threadFocused = FALSE;
    _threadMgr->IsThreadFocus(&threadFocused);
    InterlockedExchange(&_foregroundFocused, threadFocused ? 1 : 0);

    ITfSource* source = nullptr;
    if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source))) && source)
    {
        source->AdviseSink(IID_ITfThreadMgrEventSink, static_cast<ITfThreadMgrEventSink*>(this), &_threadCookie);
        source->AdviseSink(
            IID_ITfThreadFocusSink,
            static_cast<ITfThreadFocusSink*>(this),
            &_threadFocusCookie);
        source->AdviseSink(
            IID_ITfInputProcessorProfileActivationSink,
            static_cast<ITfInputProcessorProfileActivationSink*>(this),
            &_profileCookie);
        source->Release();
    }

    // 屏幕键盘上屏，不订阅 ITfKeyEventSink。一订阅，即便 pfEaten=FALSE，
    // Chromium/Cursor 在组合态也会把回车交给 TIP；旧 DLL 更会直接吞掉 VK_RETURN。
    EnsureMessageWindow();
    StartCmdPipe();
    AdviseFunctionProvider();
    const auto sipCancel = SipCancel::Enable();

    const auto sequence = InterlockedIncrement(&_stateSequence);
    char json[416] = {};
    sprintf_s(json,
        "{\"t\":\"on\",\"hwnd\":%llu,\"pid\":%u,\"doc\":%u,\"thread\":%u,"
        "\"activeFlags\":%lu,\"immersive\":%u,\"uiElementOnly\":%u,"
        "\"sipCancel\":%u,\"seq\":%ld}",
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
        GetCurrentProcessId(),
        InterlockedCompareExchange(&_documentFocused, 0, 0) ? 1u : 0u,
        InterlockedCompareExchange(&_foregroundFocused, 0, 0) ? 1u : 0u,
        static_cast<unsigned long>(activeFlags),
        (activeFlags & TF_TMF_IMMERSIVEMODE) ? 1u : 0u,
        (activeFlags & TF_TMF_UIELEMENTENABLEDONLY) ? 1u : 0u,
        sipCancel ? 1u : 0u,
        sequence);
    NotifyBackend(json);
    PublishContextState(_activeContext);
    return S_OK;
}

STDMETHODIMP T9Ime::Deactivate()
{
    InterlockedExchange(&_profileActive, 0);
    SipCancel::Disable();
    StopCmdPipe();

    const auto sequence = InterlockedIncrement(&_stateSequence);
    char json[224] = {};
    sprintf_s(json, "{\"t\":\"off\",\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
        GetCurrentProcessId(),
        sequence);
    NotifyBackend(json);

    UnbindFocusedContext();
    InterlockedIncrement(&_contextEpoch);
    UnadviseFunctionProvider();

    if (_threadMgr)
    {
        ITfKeystrokeMgr* keys = nullptr;
        if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keys))) && keys)
        {
            keys->UnadviseKeyEventSink(_clientId);
            keys->Release();
        }

        ITfSource* source = nullptr;
        if ((_threadCookie != TF_INVALID_COOKIE
                || _threadFocusCookie != TF_INVALID_COOKIE
                || _profileCookie != TF_INVALID_COOKIE) &&
            SUCCEEDED(_threadMgr->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source))) && source)
        {
            if (_threadCookie != TF_INVALID_COOKIE)
            {
                source->UnadviseSink(_threadCookie);
            }
            if (_profileCookie != TF_INVALID_COOKIE)
            {
                source->UnadviseSink(_profileCookie);
            }
            if (_threadFocusCookie != TF_INVALID_COOKIE)
            {
                source->UnadviseSink(_threadFocusCookie);
            }
            source->Release();
        }

        _threadMgr->Release();
        _threadMgr = nullptr;
    }

    HideBandHost();
    if (_bandHost)
    {
        DestroyWindow(_bandHost);
        _bandHost = nullptr;
        _bandOwner = nullptr;
        _bandHostBand = 0;
        _bandVisible = false;
        _bandX = INT_MIN;
        _bandY = INT_MIN;
    }
    if (_bandBitmap)
    {
        DeleteObject(_bandBitmap);
        _bandBitmap = nullptr;
    }
    if (_msgHwnd)
    {
        DestroyWindow(_msgHwnd);
        _msgHwnd = nullptr;
    }

    _clientId = TF_CLIENTID_NULL;
    _threadCookie = TF_INVALID_COOKIE;
    _threadFocusCookie = TF_INVALID_COOKIE;
    _profileCookie = TF_INVALID_COOKIE;
    InterlockedExchange(&_documentFocused, 0);
    InterlockedExchange(&_foregroundFocused, 0);
    InterlockedExchange(&_activeFlags, 0);
    return S_OK;
}

void T9Ime::AdviseFunctionProvider()
{
    if (!_threadMgr || _functionProviderAdvised)
    {
        return;
    }

    ITfSourceSingle* source = nullptr;
    if (SUCCEEDED(_threadMgr->QueryInterface(
            IID_ITfSourceSingle,
            reinterpret_cast<void**>(&source)))
        && source)
    {
        if (SUCCEEDED(source->AdviseSingleSink(
                _clientId,
                IID_ITfFunctionProvider,
                static_cast<ITfFunctionProvider*>(this))))
        {
            _functionProviderAdvised = true;
        }
        source->Release();
    }

    if (!_searchCandidates)
    {
        CreateSearchCandidateProvider(&_searchCandidates);
    }
}

void T9Ime::UnadviseFunctionProvider()
{
    if (_threadMgr && _functionProviderAdvised)
    {
        ITfSourceSingle* source = nullptr;
        if (SUCCEEDED(_threadMgr->QueryInterface(
                IID_ITfSourceSingle,
                reinterpret_cast<void**>(&source)))
            && source)
        {
            source->UnadviseSingleSink(_clientId, IID_ITfFunctionProvider);
            source->Release();
        }
    }

    _functionProviderAdvised = false;
    if (_searchCandidates)
    {
        _searchCandidates->Release();
        _searchCandidates = nullptr;
    }
}

STDMETHODIMP T9Ime::GetType(GUID* pguid)
{
    if (!pguid)
    {
        return E_INVALIDARG;
    }

    *pguid = CLSID_T9Ime;
    return S_OK;
}

STDMETHODIMP T9Ime::GetDescription(BSTR* pbstrDesc)
{
    if (!pbstrDesc)
    {
        return E_INVALIDARG;
    }

    *pbstrDesc = SysAllocString(T9IME_DESC);
    return *pbstrDesc ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP T9Ime::GetFunction(REFGUID rguid, REFIID riid, IUnknown** ppunk)
{
    if (!ppunk)
    {
        return E_INVALIDARG;
    }

    *ppunk = nullptr;
    if (!IsEqualGUID(rguid, GUID_NULL))
    {
        return E_NOINTERFACE;
    }

    if (IsEqualIID(riid, IID_ITfFnSearchCandidateProvider) && _searchCandidates)
    {
        NotifyBackend("{\"t\":\"fn\",\"name\":\"searchCandidates\"}");
        return _searchCandidates->QueryInterface(riid, reinterpret_cast<void**>(ppunk));
    }

    if (IsEqualIID(riid, IID_ITfFnGetPreferredTouchKeyboardLayout)
        || IsEqualIID(riid, IID_ITfFunction))
    {
        NotifyBackend("{\"t\":\"fn\",\"name\":\"touchLayout\"}");
    }

    return QueryInterface(riid, reinterpret_cast<void**>(ppunk));
}

STDMETHODIMP T9Ime::GetDisplayName(BSTR* pbstrName)
{
    if (!pbstrName)
    {
        return E_INVALIDARG;
    }

    *pbstrName = SysAllocString(T9IME_DESC);
    return *pbstrName ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP T9Ime::GetLayout(TKBLayoutType* ptkblayoutType, WORD* pwPreferredLayoutId)
{
    if (!ptkblayoutType || !pwPreferredLayoutId)
    {
        return E_INVALIDARG;
    }

    *ptkblayoutType = TKBLT_OPTIMIZED;
    *pwPreferredLayoutId = TKBL_OPT_SIMPLIFIED_CHINESE_PINYIN;
    return S_OK;
}

bool T9Ime::IsEditableDocument(ITfDocumentMgr* document)
{
    if (!document)
    {
        return false;
    }

    ITfContext* context = nullptr;
    if (FAILED(document->GetTop(&context)) || !context)
    {
        return false;
    }

    const auto editable = IsEditableContext(context);
    context->Release();
    return editable;
}

bool T9Ime::IsEditableContext(ITfContext* context)
{
    if (!context)
    {
        return false;
    }

    if (ContextCompartmentIsSet(context, GUID_COMPARTMENT_KEYBOARD_DISABLED)
        || ContextCompartmentIsSet(context, GUID_COMPARTMENT_EMPTYCONTEXT))
    {
        return false;
    }

    TF_STATUS status = {};
    const auto statusResult = context->GetStatus(&status);
    if (FAILED(statusResult))
    {
        return true;
    }

    return (status.dwDynamicFlags & TF_SD_READONLY) == 0;
}

void T9Ime::UnbindFocusedContext()
{
    if (_activeView)
    {
        _activeView->Release();
        _activeView = nullptr;
        _activeViewWindow = nullptr;
    }

    if (!_activeContext)
    {
        _textEditCookie = TF_INVALID_COOKIE;
        _textLayoutCookie = TF_INVALID_COOKIE;
        return;
    }

    ITfSource* source = nullptr;
    if (SUCCEEDED(_activeContext->QueryInterface(
            IID_ITfSource,
            reinterpret_cast<void**>(&source)))
        && source)
    {
        if (_textEditCookie != TF_INVALID_COOKIE)
        {
            source->UnadviseSink(_textEditCookie);
        }
        if (_textLayoutCookie != TF_INVALID_COOKIE)
        {
            source->UnadviseSink(_textLayoutCookie);
        }
        source->Release();
    }

    _textEditCookie = TF_INVALID_COOKIE;
    _textLayoutCookie = TF_INVALID_COOKIE;
    _activeContext->Release();
    _activeContext = nullptr;
}

void T9Ime::BindActiveView()
{
    if (!_activeContext)
    {
        return;
    }

    ITfContextView* nextView = nullptr;
    _activeContext->GetActiveView(&nextView);
    if (nextView == _activeView)
    {
        if (nextView)
        {
            nextView->Release();
        }
        if (_activeView && !_activeViewWindow)
        {
            _activeViewWindow = ResolveViewWindow(_activeView);
        }
        return;
    }

    if (_activeView)
    {
        _activeView->Release();
    }

    _activeView = nextView;
    _activeViewWindow = _activeView
        ? ResolveViewWindow(_activeView)
        : nullptr;
    if (InterlockedCompareExchange(&_profileActive, 0, 0) && _activeViewWindow)
    {
        SipCancel::BindHost(_activeViewWindow);
    }
    if (!_activeView)
    {
        return;
    }

}

void T9Ime::BindFocusedContext(ITfDocumentMgr* document)
{
    ITfContext* next = nullptr;
    if (document)
    {
        document->GetTop(&next);
    }

    if (next == _activeContext)
    {
        if (next)
        {
            next->Release();
        }
        BindActiveView();
        return;
    }

    UnbindFocusedContext();
    InterlockedIncrement(&_contextEpoch);
    _lastComposeLen = 0;
    if (_composition)
    {
        _composition->Release();
        _composition = nullptr;
    }

    _activeContext = next;
    if (!_activeContext)
    {
        return;
    }

    ITfSource* source = nullptr;
    if (SUCCEEDED(_activeContext->QueryInterface(
            IID_ITfSource,
            reinterpret_cast<void**>(&source)))
        && source)
    {
        source->AdviseSink(
            IID_ITfTextEditSink,
            static_cast<ITfTextEditSink*>(this),
            &_textEditCookie);
        source->AdviseSink(
            IID_ITfTextLayoutSink,
            static_cast<ITfTextLayoutSink*>(this),
            &_textLayoutCookie);
        source->Release();
    }
    BindActiveView();
}

void T9Ime::PublishContextState(ITfContext* context, TfEditCookie readCookie, int source)
{
    const auto active = context
        && context == _activeContext
        && IsEditableContext(context)
        && InterlockedCompareExchange(&_profileActive, 0, 0) != 0;
    InterlockedExchange(&_documentFocused, active ? 1 : 0);

    ContextGeometry geometry = {};
    const auto epoch = InterlockedCompareExchange(&_contextEpoch, 0, 0);
    if (!active)
    {
        EmitContextState(false, epoch, geometry, source);
        return;
    }

    BindActiveView();
    auto* probe = new (std::nothrow) ContextProbeSession(context, this, epoch, source);
    if (!probe)
    {
        CompleteContextProbe(context, epoch, geometry, source);
        return;
    }

    if (readCookie != TF_INVALID_EDIT_COOKIE)
    {
        probe->DoEditSession(readCookie);
        probe->Release();
        return;
    }

    HRESULT sessionResult = E_FAIL;
    const auto requestResult = context->RequestEditSession(
        _clientId,
        probe,
        TF_ES_ASYNCDONTCARE | TF_ES_READ,
        &sessionResult);
    probe->Release();
    if (FAILED(requestResult) || FAILED(sessionResult))
    {
        CompleteContextProbe(context, epoch, geometry, source);
    }
}

void T9Ime::CompleteContextProbe(
    ITfContext* context,
    LONG epoch,
    const ContextGeometry& geometry,
    int source)
{
    if (!context
        || context != _activeContext
        || epoch != InterlockedCompareExchange(&_contextEpoch, 0, 0))
    {
        return;
    }

    if (geometry.ViewWindow)
    {
        _activeViewWindow = geometry.ViewWindow;
        if (InterlockedCompareExchange(&_profileActive, 0, 0))
        {
            SipCancel::BindHost(_activeViewWindow);
        }
    }
    const auto active = IsEditableContext(context)
        && InterlockedCompareExchange(&_profileActive, 0, 0) != 0;
    if (epoch != InterlockedCompareExchange(&_contextEpoch, 0, 0)
        || context != _activeContext)
    {
        return;
    }

    InterlockedExchange(&_documentFocused, active ? 1 : 0);
    if (!active)
    {
        ContextGeometry empty = {};
        EmitContextState(false, epoch, empty, source);
        return;
    }

    EmitContextState(true, epoch, geometry, source);
}

void T9Ime::EmitContextState(
    bool active,
    LONG epoch,
    const ContextGeometry& geometry,
    int source)
{
    const auto activeFlags = static_cast<DWORD>(
        InterlockedCompareExchange(&_activeFlags, 0, 0));

    // 这里不能按内容去重。开始菜单搜索框和任务栏搜索框复用同一个 SearchHost 表面，
    // 两者的几何与标志位可以完全相同；而后端的显示判定还依赖前台窗口和 UIA 焦点等
    // 本消息之外的状态，所以“内容重复”的通知实际是驱动后端重新评估的心跳，
    // 吞掉它们会让第一次点击弹不出来、失焦也不隐藏。
    const auto sequence = InterlockedIncrement(&_stateSequence);
    char json[704] = {};
    sprintf_s(
        json,
        "{\"t\":\"context\",\"on\":%u,\"profile\":%u,\"thread\":%u,"
        "\"activeFlags\":%lu,\"immersive\":%u,\"uiElementOnly\":%u,"
        "\"epoch\":%ld,\"layout\":%u,\"src\":%d,\"sel\":%u,\"x\":%ld,\"y\":%ld,\"r\":%ld,\"b\":%ld,"
        "\"sx\":%ld,\"sy\":%ld,\"sr\":%ld,\"sb\":%ld,\"view\":%llu,"
        "\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
        active ? 1u : 0u,
        InterlockedCompareExchange(&_profileActive, 0, 0) ? 1u : 0u,
        InterlockedCompareExchange(&_foregroundFocused, 0, 0) ? 1u : 0u,
        static_cast<unsigned long>(activeFlags),
        (activeFlags & TF_TMF_IMMERSIVEMODE) ? 1u : 0u,
        (activeFlags & TF_TMF_UIELEMENTENABLEDONLY) ? 1u : 0u,
        epoch,
        geometry.HasCaret ? 1u : geometry.LayoutPending ? 2u : 0u,
        source,
        geometry.HasRangeSelection ? 1u : 0u,
        geometry.Caret.left,
        geometry.Caret.top,
        geometry.Caret.right,
        geometry.Caret.bottom,
        geometry.Screen.left,
        geometry.Screen.top,
        geometry.Screen.right,
        geometry.Screen.bottom,
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(geometry.ViewWindow)),
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
        GetCurrentProcessId(),
        sequence);
    NotifyBackend(json);
}

void T9Ime::PublishDocumentState(ITfDocumentMgr* document)
{
    BindFocusedContext(document);
    PublishContextState(_activeContext);
}

void T9Ime::RefreshDocumentState()
{
    ITfDocumentMgr* focusedDocument = nullptr;
    if (_threadMgr)
    {
        _threadMgr->GetFocus(&focusedDocument);
    }
    PublishDocumentState(focusedDocument);
    if (focusedDocument)
    {
        focusedDocument->Release();
    }
}

void T9Ime::RefreshFromKeyContext(ITfContext* context)
{
    if (!context || context == _activeContext)
    {
        return;
    }

    ITfDocumentMgr* document = nullptr;
    if (SUCCEEDED(context->GetDocumentMgr(&document)) && document)
    {
        PublishDocumentState(document);
        document->Release();
    }
}

STDMETHODIMP T9Ime::OnInitDocumentMgr(ITfDocumentMgr*)
{
    return S_OK;
}

STDMETHODIMP T9Ime::OnUninitDocumentMgr(ITfDocumentMgr*)
{
    return S_OK;
}

STDMETHODIMP T9Ime::OnSetFocus(ITfDocumentMgr* pDoc, ITfDocumentMgr*)
{
    PublishDocumentState(pDoc);
    return S_OK;
}

STDMETHODIMP T9Ime::OnPushContext(ITfContext*)
{
    RefreshDocumentState();
    return S_OK;
}

STDMETHODIMP T9Ime::OnPopContext(ITfContext*)
{
    RefreshDocumentState();
    return S_OK;
}

STDMETHODIMP T9Ime::OnEndEdit(
    ITfContext* context,
    TfEditCookie readCookie,
    ITfEditRecord*)
{
    if (context == _activeContext)
    {
        PublishContextState(context, readCookie, 2);
    }
    return S_OK;
}

STDMETHODIMP T9Ime::OnLayoutChange(
    ITfContext* context,
    TfLayoutCode code,
    ITfContextView*)
{
    if (context != _activeContext)
    {
        return S_OK;
    }

    if (code == TF_LC_DESTROY)
    {
        RefreshDocumentState();
    }
    else
    {
        PublishContextState(context, TF_INVALID_EDIT_COOKIE, 1);
    }
    return S_OK;
}

void T9Ime::PublishThreadState(bool focused)
{
    InterlockedExchange(&_foregroundFocused, focused ? 1 : 0);
    const auto sequence = InterlockedIncrement(&_stateSequence);
    char json[224] = {};
    sprintf_s(json, "{\"t\":\"focus\",\"kind\":\"thread\",\"on\":%u,\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
        focused ? 1u : 0u,
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
        GetCurrentProcessId(),
        sequence);
    NotifyBackend(json);
}

STDMETHODIMP T9Ime::OnSetThreadFocus()
{
    if (InterlockedCompareExchange(&_profileActive, 0, 0))
    {
        // CoreInputView 绑定的是取用那一刻的前台窗口，本线程重新拿到焦点就得重订。
        SipCancel::Refresh();
    }
    PublishThreadState(true);
    RefreshDocumentState();
    return S_OK;
}

STDMETHODIMP T9Ime::OnKillThreadFocus()
{
    PublishThreadState(false);
    return S_OK;
}

STDMETHODIMP T9Ime::OnSetFocus(BOOL foreground)
{
    PublishThreadState(foreground != FALSE);
    return S_OK;
}

STDMETHODIMP T9Ime::OnActivated(
    DWORD profileType,
    LANGID,
    REFCLSID clsid,
    REFGUID category,
    REFGUID profile,
    HKL,
    DWORD flags)
{
    if (category != GUID_TFCAT_TIP_KEYBOARD)
    {
        return S_OK;
    }

    const auto ours = profileType == TF_PROFILETYPE_INPUTPROCESSOR
        && clsid == CLSID_T9Ime
        && profile == GUID_T9ImeProfile;
    const auto activeFlag = (flags & TF_IPSINK_FLAG_ACTIVE) != 0;
    if (!activeFlag && !ours)
    {
        return S_OK;
    }

    const auto t9 = ours && activeFlag;
    const auto previousProfile = InterlockedExchange(&_profileActive, t9 ? 1 : 0);
    if ((previousProfile != 0) != t9)
    {
        InterlockedIncrement(&_contextEpoch);
    }
    if (t9)
    {
        SipCancel::Refresh();
    }
    else
    {
        SipCancel::Disable();
        HideBandHost();
    }
    const auto sequence = InterlockedIncrement(&_stateSequence);
    char json[256] = {};
    sprintf_s(json, "{\"t\":\"profile\",\"on\":%u,\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
        t9 ? 1u : 0u,
        static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
        GetCurrentProcessId(),
        sequence);
    NotifyBackend(json);

    if (t9)
    {
        BOOL threadFocused = FALSE;
        if (_threadMgr)
        {
            _threadMgr->IsThreadFocus(&threadFocused);
        }
        PublishThreadState(threadFocused != FALSE);
        RefreshDocumentState();
    }
    else
    {
        PublishContextState(_activeContext);
    }
    return S_OK;
}

STDMETHODIMP T9Ime::OnTestKeyDown(ITfContext* context, WPARAM, LPARAM, BOOL* pfEaten)
{
    RefreshFromKeyContext(context);
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP T9Ime::OnTestKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* pfEaten)
{
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP T9Ime::OnKeyDown(ITfContext* context, WPARAM, LPARAM, BOOL* pfEaten)
{
    RefreshFromKeyContext(context);
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP T9Ime::OnKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* pfEaten)
{
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP T9Ime::OnPreservedKey(ITfContext*, REFGUID, BOOL* pfEaten)
{
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP T9Ime::OnCompositionTerminated(TfEditCookie, ITfComposition* pComposition)
{
    if (_composition == pComposition)
    {
        _composition->Release();
        _composition = nullptr;
    }
    return S_OK;
}

void T9Ime::ApplyText(const wchar_t* text, int kind)
{
    if (kind == T9KindReturn)
    {
        PostReturnKey();
        return;
    }

    if (kind == T9KindSearchCandidates)
    {
        SetSearchCandidateCache(text);
        return;
    }

    if (kind == T9KindCompose)
    {
        text = ComposeTextFromPayload(text);
    }
    else if (kind == T9KindCommit || kind == T9KindCancel)
    {
        ClearSearchCandidateCache();
    }

    const auto result = RequestInsert(text, kind);
    if (FAILED(result))
    {
        char json[128] = {};
        sprintf_s(
            json,
            "{\"t\":\"apply\",\"kind\":%d,\"hr\":%ld,\"pid\":%u}",
            kind,
            static_cast<long>(result),
            GetCurrentProcessId());
        NotifyBackend(json);
    }
}

namespace
{
    struct ApplyPacket
    {
        int Kind;
        int Bytes;
        BYTE Data[1];
    };

    using GetBandFn = BOOL (WINAPI*)(HWND, DWORD*);
    using CreateInBandFn = HWND (WINAPI*)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int,
        HWND, HMENU, HINSTANCE, LPVOID, DWORD);
    using SetPointerCaptureFn = BOOL (WINAPI*)(HWND, UINT32);
    using ReleasePointerCaptureFn = BOOL (WINAPI*)(UINT32);

    GetBandFn FnGetBand()
    {
        return reinterpret_cast<GetBandFn>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetWindowBand"));
    }

    CreateInBandFn FnCreateInBand()
    {
        return reinterpret_cast<CreateInBandFn>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "CreateWindowInBand"));
    }

    void CapturePointer(HWND hwnd, UINT32 pointerId)
    {
        const auto fn = reinterpret_cast<SetPointerCaptureFn>(
            GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetPointerCapture"));
        if (fn)
        {
            fn(hwnd, pointerId);
        }
    }

    void ReleaseCapturedPointer(UINT32 pointerId)
    {
        const auto fn = reinterpret_cast<ReleasePointerCaptureFn>(
            GetProcAddress(GetModuleHandleW(L"user32.dll"), "ReleasePointerCapture"));
        if (fn)
        {
            fn(pointerId);
        }
    }

    bool ReadExact(HANDLE pipe, void* buffer, DWORD bytes)
    {
        auto* cursor = static_cast<BYTE*>(buffer);
        DWORD total = 0;
        while (total < bytes)
        {
            DWORD read = 0;
            if (!ReadFile(pipe, cursor + total, bytes - total, &read, nullptr) || read == 0)
            {
                return false;
            }
            total += read;
        }
        return true;
    }
}

HWND T9Ime::FindHostOwner()
{
    HWND hwnd = _activeViewWindow;
    if (hwnd)
    {
        hwnd = GetAncestor(hwnd, GA_ROOT);
    }
    if (!hwnd)
    {
        hwnd = GetForegroundWindow();
    }
    if (!hwnd)
    {
        return nullptr;
    }

    DWORD band = 0;
    const auto getBand = FnGetBand();
    if (!getBand || !getBand(hwnd, &band) || band <= 1)
    {
        return nullptr;
    }

    // SearchHost can own the visible edit surface while TSF instantiates us in
    // Explorer/StartMenuExperienceHost. Making the Band window an owned popup
    // keeps clicks inside the flyout instead of dismissing the Start menu.
    return hwnd;
}

DWORD T9Ime::FindHostBand()
{
    auto getBand = FnGetBand();
    if (!getBand)
    {
        return 13;
    }

    DWORD band = 0;
    HWND view = _activeViewWindow;
    if (view)
    {
        view = GetAncestor(view, GA_ROOT);
    }
    if (view && getBand(view, &band) && band > 1)
    {
        return band;
    }

    band = 0;
    if (const auto foreground = GetForegroundWindow())
    {
        getBand(foreground, &band);
        if (band > 1)
        {
            // CreateWindowInBand rejects higher privileged bands from SearchHost.
            // A topmost owned popup in the visible surface's own band is the
            // supported ordering available to this in-process TSF component.
            return band;
        }
    }

    DWORD best = 0;
    EnumWindows([](HWND hwnd, LPARAM lp) -> BOOL
    {
        DWORD pid = 0;
        GetWindowThreadProcessId(hwnd, &pid);
        if (pid != GetCurrentProcessId() || !IsWindowVisible(hwnd))
        {
            return TRUE;
        }

        auto getBand = FnGetBand();
        DWORD b = 0;
        if (getBand && getBand(hwnd, &b) && b > *reinterpret_cast<DWORD*>(lp))
        {
            *reinterpret_cast<DWORD*>(lp) = b;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&best));

    return best > 1 ? best : 13;
}

POINT T9Ime::MapBandPointer(HWND hwnd, POINT client) const
{
    RECT rc = {};
    if (!GetClientRect(hwnd, &rc)
        || rc.right <= 0
        || rc.bottom <= 0
        || _bandFrameWidth <= 0
        || _bandFrameHeight <= 0
        || (rc.right == _bandFrameWidth && rc.bottom == _bandFrameHeight))
    {
        return client;
    }

    POINT mapped = {
        MulDiv(client.x, _bandFrameWidth, rc.right),
        MulDiv(client.y, _bandFrameHeight, rc.bottom)
    };
    return mapped;
}

LRESULT CALLBACK T9Ime::BandProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_TABLET_QUERYSYSTEMGESTURESTATUS)
    {
        return TABLET_DISABLE_PRESSANDHOLD |
            TABLET_DISABLE_PENTAPFEEDBACK |
            TABLET_DISABLE_PENBARRELFEEDBACK |
            TABLET_DISABLE_FLICKS;
    }

    if (msg == WM_NCHITTEST)
    {
        return HTCLIENT;
    }

    if (msg == WM_MOUSEACTIVATE)
    {
        return MA_NOACTIVATE;
    }
    if (msg == WM_POINTERACTIVATE)
    {
        return PA_NOACTIVATE;
    }

    auto* self = reinterpret_cast<T9Ime*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (msg == WM_ERASEBKGND)
    {
        return 1;
    }
    if (self && msg == WM_PAINT)
    {
        PAINTSTRUCT ps = {};
        const auto target = BeginPaint(hwnd, &ps);
        if (target && self->_bandBitmap)
        {
            const auto source = CreateCompatibleDC(target);
            if (source)
            {
                const auto old = SelectObject(source, self->_bandBitmap);
                BitBlt(
                    target,
                    0,
                    0,
                    self->_bandFrameWidth,
                    self->_bandFrameHeight,
                    source,
                    0,
                    0,
                    SRCCOPY);
                SelectObject(source, old);
                DeleteDC(source);
            }
        }
        EndPaint(hwnd, &ps);
        return 0;
    }
    if (self && msg == WM_POINTERDOWN)
    {
        POINTER_INFO info = {};
        const auto pointerId = GET_POINTERID_WPARAM(wParam);
        if (GetPointerInfo(pointerId, &info))
        {
            auto client = info.ptPixelLocation;
            ScreenToClient(hwnd, &client);
            client = self->MapBandPointer(hwnd, client);
            self->_bandPointerDown = client;
            self->_bandPointerActive = true;
            self->_bandPointerId = pointerId;
            self->_bandDragging =
                self->_bandFrameWidth > 0 &&
                self->_bandFrameHeight > 0 &&
                client.x < self->_bandFrameWidth / 6 &&
                client.y < self->_bandFrameHeight * 36 / 360;
            if (self->_bandDragging)
            {
                RECT rect = {};
                self->_bandDragCursor = info.ptPixelLocation;
                if (GetWindowRect(hwnd, &rect))
                {
                    self->_bandDragWindow.x = rect.left;
                    self->_bandDragWindow.y = rect.top;
                }
            }
            CapturePointer(hwnd, pointerId);
            if (!self->_bandDragging)
            {
                // 按下即上报：后端决定这个键是否属于“按下即触发”。
                char json[160] = {};
                wsprintfA(json, "{\"t\":\"press\",\"x\":%d,\"y\":%d}", client.x, client.y);
                self->NotifyBackend(json);
            }
        }
        return 0;
    }

    if (self && msg == WM_POINTERUPDATE && self->_bandPointerActive)
    {
        POINTER_INFO info = {};
        const auto pointerId = GET_POINTERID_WPARAM(wParam);
        if (pointerId == self->_bandPointerId
            && self->_bandDragging
            && GetPointerInfo(pointerId, &info))
        {
            POINT target = {
                self->_bandDragWindow.x + info.ptPixelLocation.x - self->_bandDragCursor.x,
                self->_bandDragWindow.y + info.ptPixelLocation.y - self->_bandDragCursor.y
            };
            if (self->_bandChild && self->_bandOwner)
            {
                ScreenToClient(self->_bandOwner, &target);
            }
            SetWindowPos(
                hwnd,
                self->_bandChild ? HWND_TOP : HWND_TOPMOST,
                target.x,
                target.y,
                0,
                0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        return 0;
    }

    if (self && msg == WM_POINTERUP)
    {
        POINTER_INFO info = {};
        const auto pointerId = GET_POINTERID_WPARAM(wParam);
        if (pointerId != self->_bandPointerId || !GetPointerInfo(pointerId, &info))
        {
            return 0;
        }

        auto client = info.ptPixelLocation;
        ScreenToClient(hwnd, &client);
        client = self->MapBandPointer(hwnd, client);
        char json[160] = {};
        if (self->_bandDragging)
        {
            RECT rect = {};
            GetWindowRect(hwnd, &rect);
            wsprintfA(json, "{\"t\":\"moved\",\"x\":%d,\"y\":%d}", rect.left, rect.top);
        }
        else
        {
            const int dx = client.x - self->_bandPointerDown.x;
            const int dy = client.y - self->_bandPointerDown.y;
            if (max(abs(dx), abs(dy)) >= 28)
            {
                wsprintfA(json,
                    "{\"t\":\"swipe\",\"x1\":%d,\"y1\":%d,\"x2\":%d,\"y2\":%d}",
                    self->_bandPointerDown.x,
                    self->_bandPointerDown.y,
                    client.x,
                    client.y);
            }
            else
            {
                wsprintfA(json, "{\"t\":\"hit\",\"x\":%d,\"y\":%d}", client.x, client.y);
            }
        }

        ReleaseCapturedPointer(pointerId);
        self->_bandDragging = false;
        self->_bandPointerActive = false;
        self->_bandPointerId = 0;
        self->NotifyBackend(json);
        return 0;
    }

    if (self && msg == WM_POINTERCAPTURECHANGED)
    {
        const auto wasActive = self->_bandPointerActive && !self->_bandDragging;
        self->_bandDragging = false;
        self->_bandPointerActive = false;
        self->_bandPointerId = 0;
        if (wasActive)
        {
            self->NotifyBackend("{\"t\":\"release\"}");
        }
        return 0;
    }

    if (self && msg == WM_LBUTTONDOWN)
    {
        POINT down = {
            static_cast<short>(LOWORD(lParam)),
            static_cast<short>(HIWORD(lParam))
        };
        down = self->MapBandPointer(hwnd, down);
        self->_bandPointerDown = down;
        self->_bandPointerActive = true;
        self->_bandDragging =
            self->_bandFrameWidth > 0 &&
            self->_bandFrameHeight > 0 &&
            self->_bandPointerDown.x < self->_bandFrameWidth / 6 &&
            self->_bandPointerDown.y < self->_bandFrameHeight * 36 / 360;
        if (self->_bandDragging)
        {
            RECT rect = {};
            GetCursorPos(&self->_bandDragCursor);
            if (GetWindowRect(hwnd, &rect))
            {
                self->_bandDragWindow.x = rect.left;
                self->_bandDragWindow.y = rect.top;
            }
        }
        SetCapture(hwnd);
        if (!self->_bandDragging)
        {
            char json[160] = {};
            wsprintfA(json,
                "{\"t\":\"press\",\"x\":%d,\"y\":%d}",
                self->_bandPointerDown.x,
                self->_bandPointerDown.y);
            self->NotifyBackend(json);
        }
        return 0;
    }

    if (self && msg == WM_MOUSEMOVE && self->_bandDragging && GetCapture() == hwnd)
    {
        POINT cursor = {};
        if (GetCursorPos(&cursor))
        {
            POINT target = {
                self->_bandDragWindow.x + cursor.x - self->_bandDragCursor.x,
                self->_bandDragWindow.y + cursor.y - self->_bandDragCursor.y
            };
            if (self->_bandChild && self->_bandOwner)
            {
                ScreenToClient(self->_bandOwner, &target);
            }
            SetWindowPos(
                hwnd,
                self->_bandChild ? HWND_TOP : HWND_TOPMOST,
                target.x,
                target.y,
                0,
                0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        return 0;
    }

    if (self && msg == WM_LBUTTONUP)
    {
        if (GetCapture() == hwnd)
        {
            ReleaseCapture();
        }
        POINT up = {
            static_cast<short>(LOWORD(lParam)),
            static_cast<short>(HIWORD(lParam))
        };
        up = self->MapBandPointer(hwnd, up);
        const int x = up.x;
        const int y = up.y;
        if (self->_bandDragging)
        {
            RECT rect = {};
            GetWindowRect(hwnd, &rect);
            char moved[128] = {};
            wsprintfA(moved, "{\"t\":\"moved\",\"x\":%d,\"y\":%d}", rect.left, rect.top);
            self->_bandDragging = false;
            self->_bandPointerActive = false;
            self->NotifyBackend(moved);
            return 0;
        }

        const int dx = x - self->_bandPointerDown.x;
        const int dy = y - self->_bandPointerDown.y;
        char json[160] = {};
        if (self->_bandPointerActive && max(abs(dx), abs(dy)) >= 28)
        {
            wsprintfA(json,
                "{\"t\":\"swipe\",\"x1\":%d,\"y1\":%d,\"x2\":%d,\"y2\":%d}",
                self->_bandPointerDown.x,
                self->_bandPointerDown.y,
                x,
                y);
        }
        else
        {
            wsprintfA(json, "{\"t\":\"hit\",\"x\":%d,\"y\":%d}", x, y);
        }
        self->_bandPointerActive = false;
        self->NotifyBackend(json);
        return 0;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

bool T9Ime::EnsureBandHost()
{
    const auto owner = FindHostOwner();
    const auto band = FindHostBand();
    const auto useChild = false;
    if (_bandHost
        && IsWindow(_bandHost)
        && _bandHostBand == band
        && _bandOwner == owner
        && _bandChild == useChild)
    {
        return true;
    }

    if (_bandHost)
    {
        DestroyWindow(_bandHost);
        _bandHost = nullptr;
        _bandVisible = false;
        _bandX = INT_MIN;
        _bandY = INT_MIN;
        _bandFrameWidth = 0;
        _bandFrameHeight = 0;
    }

    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = BandProc;
    wc.hInstance = g_hInst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.lpszClassName = L"T9Ime.BandHost";
    RegisterClassExW(&wc);

    if (useChild)
    {
        const DWORD ex = WS_EX_NOACTIVATE | WS_EX_LAYERED;
        _bandHost = CreateWindowExW(
            ex,
            L"T9Ime.BandHost",
            L"T9 九键",
            WS_CHILD,
            0,
            0,
            1,
            1,
            owner,
            nullptr,
            g_hInst,
            nullptr);
    }
    else
    {
        auto create = FnCreateInBand();
        if (!create)
        {
            SetLastError(ERROR_PROC_NOT_FOUND);
            return false;
        }
        const DWORD ex =
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        _bandHost = create(ex, L"T9Ime.BandHost", L"T9 九键", WS_POPUP, 0, 0, 1, 1,
            owner, nullptr, g_hInst, nullptr, band);
    }

    if (!_bandHost)
    {
        return false;
    }

    // Microsoft requires IME candidate UI to be owned by the active context
    // view (falling back to GetFocus). The pending-first-show gate prevents us
    // from binding to a retiring SearchHost root during surface handoff.

    DWORD actualBand = 0;
    if (auto getBand = FnGetBand())
    {
        getBand(_bandHost, &actualBand);
    }
    _bandHostBand = actualBand ? actualBand : band;
    _bandOwner = owner;
    _bandChild = useChild;
    SetWindowLongPtrW(_bandHost, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
    DisablePressAndHold(_bandHost);
    return true;
}

void T9Ime::HideBandHost()
{
    const auto wasVisible = _bandVisible;
    if (_bandHost && IsWindow(_bandHost))
    {
        ShowWindow(_bandHost, SW_HIDE);
    }
    if (wasVisible)
    {
        _bandVisible = false;
        _bandX = INT_MIN;
        _bandY = INT_MIN;
        NotifyWinEvent(
            EVENT_OBJECT_IME_HIDE,
            _bandHost,
            OBJID_CLIENT,
            CHILDID_SELF);
        char json[192] = {};
        sprintf_s(json, "{\"t\":\"host\",\"on\":0,\"err\":0,\"client\":%llu,\"pid\":%u}",
            static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
            GetCurrentProcessId());
        NotifyBackend(json);
    }
}

bool T9Ime::BlitFrame(int x, int y, int width, int height, const BYTE* pixels)
{
    if (!pixels || width < 8 || height < 8 || !EnsureBandHost())
    {
        return false;
    }

    HDC screen = GetDC(nullptr);
    if (!screen)
    {
        return false;
    }

    HDC mem = CreateCompatibleDC(screen);
    if (!mem)
    {
        ReleaseDC(nullptr, screen);
        return false;
    }
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = width;
    bi.bmiHeader.biHeight = -height;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HBITMAP bmp = CreateDIBSection(mem, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (!bmp || !bits)
    {
        if (mem)
        {
            DeleteDC(mem);
        }
        ReleaseDC(nullptr, screen);
        return false;
    }

    memcpy(bits, pixels, static_cast<size_t>(width) * height * 4);
    POINT dst = { x, y };
    if (_bandChild && _bandOwner)
    {
        ScreenToClient(_bandOwner, &dst);
    }
    const auto oldBitmap = SelectObject(mem, bmp);
    POINT source = {};
    SIZE size = { width, height };
    BLENDFUNCTION blend = {};
    blend.BlendOp = AC_SRC_OVER;
    blend.SourceConstantAlpha = 255;
    blend.AlphaFormat = AC_SRC_ALPHA;
    // 位置没变时不要带 pptDst，避免分层窗微抖。
    // psize 必须每次都传：Win11 在 psize=NULL 时会成功返回却不换像素，
    // 搜索里的 HostRender 就会停在第一张静止图上，按键缩放和候选都看不见。
    const auto moved = _bandX != dst.x || _bandY != dst.y
        || _bandFrameWidth != width || _bandFrameHeight != height;
    const auto updated = UpdateLayeredWindow(
        _bandHost,
        screen,
        moved ? &dst : nullptr,
        &size,
        mem,
        &source,
        0,
        &blend,
        ULW_ALPHA);
    if (updated)
    {
        _bandX = dst.x;
        _bandY = dst.y;
        _bandFrameWidth = width;
        _bandFrameHeight = height;
    }
    SelectObject(mem, oldBitmap);
    if (_bandBitmap)
    {
        DeleteObject(_bandBitmap);
    }
    _bandBitmap = bmp;
    // 分层位图可以比 HWND 大：底下的键看得见，点击却穿到搜索联想上。
    // 触摸层必须跟帧一样高，位置未变时才钉住，避免每次刷新微抖。
    const auto raised = updated && SetWindowPos(
        _bandHost,
        _bandChild ? HWND_TOP : HWND_TOPMOST,
        dst.x,
        dst.y,
        width,
        height,
        SWP_NOACTIVATE | SWP_SHOWWINDOW | (moved ? 0u : SWP_NOMOVE));
    DeleteDC(mem);
    ReleaseDC(nullptr, screen);
    return raised == TRUE;
}

void T9Ime::HandleLift(const wchar_t* spec)
{
    if (!spec || !spec[0] || spec[0] == L'h' || spec[0] == L'0')
    {
        HideBandHost();
    }
}

void T9Ime::HandleFrame(const void* data, int bytes)
{
    if (!data || bytes < 16)
    {
        return;
    }

    const auto* header = static_cast<const int*>(data);
    const int x = header[0];
    const int y = header[1];
    const int w = header[2];
    const int h = header[3];
    if (w < 8 || h < 8)
    {
        return;
    }

    const auto pixelBytes = static_cast<size_t>(w) * static_cast<size_t>(h) * 4u;
    const auto need = 16u + pixelBytes;
    if (pixelBytes > 16u * 1024u * 1024u || static_cast<size_t>(bytes) < need)
    {
        return;
    }

    const auto wasVisible = _bandVisible;
    if (BlitFrame(x, y, w, h, static_cast<const BYTE*>(data) + 16))
    {
        _bandVisible = true;
        NotifyWinEvent(
            wasVisible ? EVENT_OBJECT_IME_CHANGE : EVENT_OBJECT_IME_SHOW,
            _bandHost,
            OBJID_CLIENT,
            CHILDID_SELF);
        if (!wasVisible)
        {
            char json[320] = {};
            sprintf_s(json,
                "{\"t\":\"host\",\"on\":1,\"band\":%u,\"child\":%u,"
                "\"hwnd\":%llu,\"owner\":%llu,\"view\":%llu,"
                "\"client\":%llu,\"pid\":%u}",
                _bandHostBand,
                _bandChild ? 1u : 0u,
                static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_bandHost)),
                static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_bandOwner)),
                static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_activeViewWindow)),
                static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
                GetCurrentProcessId());
            NotifyBackend(json);
        }
    }
    else
    {
        const auto error = GetLastError();
        char json[192] = {};
        sprintf_s(json, "{\"t\":\"host\",\"on\":0,\"err\":%u,\"client\":%llu,\"pid\":%u}",
            error,
            static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(_msgHwnd)),
            GetCurrentProcessId());
        NotifyBackend(json);
    }
}

bool T9Ime::EnsureMessageWindow()
{
    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = WndProc;
    wc.hInstance = g_hInst;
    wc.lpszClassName = T9IME_MSG_CLASS;
    RegisterClassExW(&wc);

    _msgHwnd = CreateWindowExW(0, T9IME_MSG_CLASS, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_hInst, this);
    return _msgHwnd != nullptr;
}

LRESULT CALLBACK T9Ime::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_NCCREATE)
    {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
    }

    auto* self = reinterpret_cast<T9Ime*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (msg == WmT9Apply && self)
    {
        auto* packet = reinterpret_cast<ApplyPacket*>(lParam);
        if (packet)
        {
            if (packet->Kind == T9KindFrame)
            {
                self->HandleFrame(packet->Data, packet->Bytes);
            }
            else if (packet->Kind == T9KindQueryState)
            {
                ITfDocumentMgr* focusedDocument = nullptr;
                if (self->_threadMgr)
                {
                    self->_threadMgr->GetFocus(&focusedDocument);
                }
                InterlockedExchange(
                    &self->_documentFocused,
                    self->IsEditableDocument(focusedDocument) ? 1 : 0);
                if (focusedDocument)
                {
                    focusedDocument->Release();
                }

                BOOL threadFocused = FALSE;
                if (self->_threadMgr)
                {
                    self->_threadMgr->IsThreadFocus(&threadFocused);
                }
                InterlockedExchange(&self->_foregroundFocused, threadFocused ? 1 : 0);

                char json[256] = {};
                auto sequence = InterlockedIncrement(&self->_stateSequence);
                sprintf_s(json, "{\"t\":\"focus\",\"kind\":\"doc\",\"on\":%u,\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
                    InterlockedCompareExchange(&self->_documentFocused, 0, 0) ? 1u : 0u,
                    static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(self->_msgHwnd)),
                    GetCurrentProcessId(),
                    sequence);
                self->NotifyBackend(json);
                sequence = InterlockedIncrement(&self->_stateSequence);
                sprintf_s(json, "{\"t\":\"focus\",\"kind\":\"thread\",\"on\":%u,\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
                    InterlockedCompareExchange(&self->_foregroundFocused, 0, 0) ? 1u : 0u,
                    static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(self->_msgHwnd)),
                    GetCurrentProcessId(),
                    sequence);
                self->NotifyBackend(json);
                sequence = InterlockedIncrement(&self->_stateSequence);
                sprintf_s(json, "{\"t\":\"profile\",\"on\":%u,\"hwnd\":%llu,\"pid\":%u,\"seq\":%ld}",
                    InterlockedCompareExchange(&self->_profileActive, 0, 0) ? 1u : 0u,
                    static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(self->_msgHwnd)),
                    GetCurrentProcessId(),
                    sequence);
                self->NotifyBackend(json);
            }
            else if (packet->Kind == T9KindLift)
            {
                self->HandleLift(reinterpret_cast<const wchar_t*>(packet->Data));
            }
            else
            {
                self->ApplyText(reinterpret_cast<const wchar_t*>(packet->Data), packet->Kind);
            }

            delete[] reinterpret_cast<BYTE*>(packet);
        }
        return 0;
    }

    if (msg == WM_COPYDATA && self)
    {
        auto* cds = reinterpret_cast<COPYDATASTRUCT*>(lParam);
        if (cds && cds->lpData)
        {
            const auto kind = static_cast<int>(cds->dwData);
            if (kind == T9KindFrame)
            {
                self->HandleFrame(cds->lpData, cds->cbData);
            }
            else if (kind == T9KindLift)
            {
                self->HandleLift(static_cast<const wchar_t*>(cds->lpData));
            }
            else
            {
                self->ApplyText(static_cast<const wchar_t*>(cds->lpData), kind);
            }
        }
        return TRUE;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

void T9Ime::QueuePacket(int kind, const void* data, int bytes)
{
    if (!_msgHwnd || bytes < 0)
    {
        return;
    }

    const auto size = static_cast<size_t>(offsetof(ApplyPacket, Data) + (bytes > 0 ? bytes : 2));
    auto* packet = reinterpret_cast<ApplyPacket*>(new (std::nothrow) BYTE[size]);
    if (!packet)
    {
        return;
    }

    packet->Kind = kind;
    packet->Bytes = bytes;
    if (data && bytes > 0)
    {
        memcpy(packet->Data, data, static_cast<size_t>(bytes));
    }
    else
    {
        packet->Data[0] = 0;
        packet->Data[1] = 0;
        packet->Bytes = 2;
    }

    if (!PostMessageW(_msgHwnd, WmT9Apply, 0, reinterpret_cast<LPARAM>(packet)))
    {
        delete[] reinterpret_cast<BYTE*>(packet);
    }
}

void T9Ime::QueueApply(int kind, const wchar_t* text)
{
    const auto n = text ? wcslen(text) : 0;
    QueuePacket(kind, text ? text : L"", static_cast<int>((n + 1) * sizeof(wchar_t)));
}

void T9Ime::StartCmdPipe()
{
    if (_cmdThread)
    {
        return;
    }

    _cmdStop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    _cmdThread = CreateThread(nullptr, 0, CmdPipeThread, this, 0, nullptr);
}

void T9Ime::StopCmdPipe()
{
    if (_cmdStop)
    {
        SetEvent(_cmdStop);
    }

    if (_cmdClient)
    {
        CancelIoEx(_cmdClient, nullptr);
        CloseHandle(_cmdClient);
        _cmdClient = nullptr;
    }

    if (_cmdThread)
    {
        WaitForSingleObject(_cmdThread, INFINITE);
        CloseHandle(_cmdThread);
        _cmdThread = nullptr;
    }

    if (_cmdStop)
    {
        CloseHandle(_cmdStop);
        _cmdStop = nullptr;
    }
}

DWORD WINAPI T9Ime::CmdPipeThread(PVOID param)
{
    auto* self = static_cast<T9Ime*>(param);
    if (!self || !self->_cmdStop)
    {
        return 0;
    }

    while (WaitForSingleObject(self->_cmdStop, 0) != WAIT_OBJECT_0)
    {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        const wchar_t* pipeNames[] = { T9IME_CMD_HOST_LOCAL, T9IME_CMD_HOST };
        for (const auto* pipeName : pipeNames)
        {
            WaitNamedPipeW(pipeName, 200);
            pipe = CreateFileW(
                pipeName,
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_EXISTING,
                0,
                nullptr);
            if (pipe != INVALID_HANDLE_VALUE)
            {
                break;
            }
        }
        if (pipe == INVALID_HANDLE_VALUE)
        {
            if (WaitForSingleObject(self->_cmdStop, 80) == WAIT_OBJECT_0)
            {
                break;
            }
            continue;
        }

        self->_cmdClient = pipe;
        DWORD pid = GetCurrentProcessId();
        const auto hwnd = static_cast<ULONGLONG>(reinterpret_cast<ULONG_PTR>(self->_msgHwnd));
        BYTE hello[12] = {};
        memcpy(hello, &pid, sizeof(pid));
        memcpy(hello + sizeof(pid), &hwnd, sizeof(hwnd));
        DWORD written = 0;
        if (!WriteFile(pipe, hello, sizeof(hello), &written, nullptr) || written != sizeof(hello))
        {
            CloseHandle(pipe);
            self->_cmdClient = nullptr;
            WaitForSingleObject(self->_cmdStop, 200);
            continue;
        }

        while (WaitForSingleObject(self->_cmdStop, 0) != WAIT_OBJECT_0)
        {
            int header[2] = {};
            if (!ReadExact(pipe, header, sizeof(header)))
            {
                break;
            }

            const auto kind = header[0];
            const auto bytes = header[1];
            if (bytes < 0 || bytes > 16 * 1024 * 1024)
            {
                break;
            }

            auto* payload = new (std::nothrow) BYTE[bytes > 0 ? bytes : 2];
            if (!payload)
            {
                break;
            }

            if (bytes > 0 && !ReadExact(pipe, payload, static_cast<DWORD>(bytes)))
            {
                delete[] payload;
                break;
            }

            self->QueuePacket(kind, payload, bytes);
            delete[] payload;
        }

        if (self->_cmdClient == pipe)
        {
            self->_cmdClient = nullptr;
        }
        CloseHandle(pipe);
        WaitForSingleObject(self->_cmdStop, 200);
    }

    return 0;
}

namespace
{
    // 触摸键的按下 / 抬起就走这条通道，连接耗时直接体现为按键延迟。
    // 因此先直接 CreateFile，只有确实“实例都忙”时才去等，
    // 并用递增退避取代固定 Sleep(75)。
    HANDLE TryOpenNotifyPipe(const wchar_t* pipeName, bool allowWait)
    {
        auto pipe = CreateFileW(
            pipeName,
            GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);
        if (pipe != INVALID_HANDLE_VALUE)
        {
            return pipe;
        }

        if (!allowWait || GetLastError() != ERROR_PIPE_BUSY)
        {
            return INVALID_HANDLE_VALUE;
        }

        if (!WaitNamedPipeW(pipeName, 100))
        {
            return INVALID_HANDLE_VALUE;
        }

        return CreateFileW(
            pipeName,
            GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);
    }

    void SendNotify(const char* json)
    {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        DWORD backoff = 1;
        for (int attempt = 0; attempt < 20 && pipe == INVALID_HANDLE_VALUE; ++attempt)
        {
            const wchar_t* pipeNames[] = { T9IME_PIPE_LOCAL, T9IME_PIPE };
            for (const auto* pipeName : pipeNames)
            {
                pipe = TryOpenNotifyPipe(pipeName, attempt > 0);
                if (pipe != INVALID_HANDLE_VALUE)
                {
                    break;
                }
            }

            if (pipe != INVALID_HANDLE_VALUE)
            {
                break;
            }

            // 后端为了保序一次只挂一个监听实例，上一条刚读完、新实例还没挂起时
            // 会短暂连不上，而这段空窗只有几十微秒。先让出时间片快速重试：
            // Sleep(1) 实际要睡满一个调度周期（约 15ms），连打时每条通知都赔上
            // 这么久，就成了肉眼可见的延迟。
            if (attempt < 8)
            {
                SwitchToThread();
                continue;
            }

            if (attempt == 8)
            {
                LaunchBackend();
            }

            Sleep(backoff);
            if (backoff < 128)
            {
                backoff *= 2;
            }
        }

        if (pipe == INVALID_HANDLE_VALUE)
        {
            return;
        }

        DWORD written = 0;
        WriteFile(pipe, json, static_cast<DWORD>(strlen(json)), &written, nullptr);
        CloseHandle(pipe);
    }

    // 按下 / 抬起必须按产生顺序送达。
    //
    // 原先每条通知各起一个线程池任务，而后端为了保序一次只挂一个管道实例，
    // 于是这些任务互相抢连接、抢不到的退避重试，送达顺序最终由线程调度决定：
    // 抬起可能跑到按下前面。后端的按下闸门只有一个 bool，乱序就会把该执行的
    // 抬起当成该丢弃的，表现为快速连打吞字、按下动画不出现。
    //
    // 改成单线程按队列串行发送，顺序回到产生顺序，也不再自己跟自己抢管道。
    struct NotifyNode
    {
        char* json;
        NotifyNode* next;
    };

    CRITICAL_SECTION g_notifyGate;
    HANDLE g_notifySignal = nullptr;
    NotifyNode* g_notifyHead = nullptr;
    NotifyNode* g_notifyTail = nullptr;
    int g_notifyDepth = 0;
    INIT_ONCE g_notifyOnce = INIT_ONCE_STATIC_INIT;

    // 后端卡死时不能无限堆积；正常连打的积压远到不了这个深度。
    const int kNotifyMaxDepth = 512;

    DWORD WINAPI NotifyPump(PVOID)
    {
        for (;;)
        {
            WaitForSingleObject(g_notifySignal, INFINITE);
            for (;;)
            {
                EnterCriticalSection(&g_notifyGate);
                auto* node = g_notifyHead;
                if (node)
                {
                    g_notifyHead = node->next;
                    if (!g_notifyHead)
                    {
                        g_notifyTail = nullptr;
                    }
                    --g_notifyDepth;
                }
                LeaveCriticalSection(&g_notifyGate);

                if (!node)
                {
                    break;
                }

                SendNotify(node->json);
                delete[] node->json;
                delete node;
            }
        }
    }

    BOOL CALLBACK StartNotifyPump(PINIT_ONCE, PVOID, PVOID*)
    {
        InitializeCriticalSection(&g_notifyGate);
        g_notifySignal = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!g_notifySignal)
        {
            DeleteCriticalSection(&g_notifyGate);
            return FALSE;
        }

        // 泵线程活到进程结束，所以把模块钉住，
        // 免得 DLL 先被卸载、线程再回到已经释放的代码上。
        HMODULE self = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&StartNotifyPump),
            &self);

        auto* thread = CreateThread(nullptr, 0, NotifyPump, nullptr, 0, nullptr);
        if (!thread)
        {
            CloseHandle(g_notifySignal);
            g_notifySignal = nullptr;
            DeleteCriticalSection(&g_notifyGate);
            return FALSE;
        }

        CloseHandle(thread);
        return TRUE;
    }
}

void T9Ime::NotifyBackend(const char* json)
{
    if (!json)
    {
        return;
    }

    if (!InitOnceExecuteOnce(&g_notifyOnce, StartNotifyPump, nullptr, nullptr))
    {
        return;
    }

    const auto len = strlen(json);
    auto* copy = new (std::nothrow) char[len + 1];
    if (!copy)
    {
        return;
    }

    memcpy(copy, json, len + 1);
    auto* node = new (std::nothrow) NotifyNode{ copy, nullptr };
    if (!node)
    {
        delete[] copy;
        return;
    }

    EnterCriticalSection(&g_notifyGate);
    const bool overflow = g_notifyDepth >= kNotifyMaxDepth;
    if (!overflow)
    {
        if (g_notifyTail)
        {
            g_notifyTail->next = node;
        }
        else
        {
            g_notifyHead = node;
        }

        g_notifyTail = node;
        ++g_notifyDepth;
    }
    LeaveCriticalSection(&g_notifyGate);

    if (overflow)
    {
        delete[] copy;
        delete node;
        return;
    }

    SetEvent(g_notifySignal);
}

bool T9Ime::EnsureBackend()
{
    if (WaitNamedPipeW(T9IME_PIPE_LOCAL, 1)
        || WaitNamedPipeW(T9IME_PIPE, 1))
    {
        return true;
    }

    LaunchBackend();
    return false;
}

bool T9Ime::IsCurrentContext(ITfContext* context)
{
    return context
        && context == _activeContext
        && InterlockedCompareExchange(&_profileActive, 0, 0) != 0;
}

void T9Ime::PostReturnKey()
{
    HWND window = _activeViewWindow;
    if (!window || !IsWindow(window))
    {
        window = GetFocus();
    }
    if ((!window || !IsWindow(window)) && _activeView)
    {
        _activeView->GetWnd(&window);
    }
    if (!window || !IsWindow(window))
    {
        return;
    }

    // 只投 KEYDOWN/KEYUP。目标线程的 TranslateMessage 会生成 WM_CHAR；
    // 再 Post 一次 WM_CHAR，Word 会插两个段落。
    const LPARAM scan = static_cast<LPARAM>(MapVirtualKeyW(VK_RETURN, MAPVK_VK_TO_VSC)) << 16;
    PostMessageW(window, WM_KEYDOWN, VK_RETURN, 1 | scan);
    PostMessageW(window, WM_KEYUP, VK_RETURN, 0xC0000001 | scan);
}

HRESULT T9Ime::RequestInsert(const wchar_t* text, int kind)
{
    if (!_threadMgr)
    {
        return E_FAIL;
    }

    ITfDocumentMgr* doc = nullptr;
    if (FAILED(_threadMgr->GetFocus(&doc)) || !doc)
    {
        return E_FAIL;
    }

    ITfContext* ctx = nullptr;
    doc->GetTop(&ctx);
    if (!ctx)
    {
        doc->Release();
        return E_FAIL;
    }
    if (InterlockedCompareExchange(&_profileActive, 0, 0) == 0)
    {
        doc->Release();
        ctx->Release();
        return E_ABORT;
    }
    if (ctx != _activeContext)
    {
        BindFocusedContext(doc);
    }
    doc->Release();
    if (ctx != _activeContext)
    {
        ctx->Release();
        RefreshDocumentState();
        return E_ABORT;
    }

    auto run = [&](DWORD flags) -> HRESULT
    {
        auto* session = new (std::nothrow) EditSession(ctx, this, text, kind, &_composition, &_lastComposeLen);
        if (!session)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT hrSession = S_OK;
        const auto hr = ctx->RequestEditSession(_clientId, session, flags, &hrSession);
        session->Release();
        return FAILED(hr) ? hr : hrSession;
    };

    auto hr = run(TF_ES_SYNC | TF_ES_READWRITE);
    if (FAILED(hr))
    {
        hr = run(TF_ES_ASYNCDONTCARE | TF_ES_READWRITE);
    }

    ctx->Release();
    return hr;
}
