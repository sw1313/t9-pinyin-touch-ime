#include "SipCancel.h"

#include <windows.h>
#include <hstring.h>
#include <msctf.h>
#include <new>
#include <shobjidl.h>
#include <windows.foundation.h>
#include <windows.ui.viewmanagement.core.h>

using ABI::Windows::Foundation::ITypedEventHandler;
using ABI::Windows::UI::ViewManagement::Core::CoreInputView;
using ABI::Windows::UI::ViewManagement::Core::CoreInputViewShowingEventArgs;
using ABI::Windows::UI::ViewManagement::Core::ICoreInputView;
using ABI::Windows::UI::ViewManagement::Core::ICoreInputView3;
using ABI::Windows::UI::ViewManagement::Core::ICoreInputView4;
using ABI::Windows::UI::ViewManagement::Core::ICoreInputViewShowingEventArgs;
using ABI::Windows::UI::ViewManagement::Core::ICoreInputViewStatics;

namespace
{

using ShowingHandler = ITypedEventHandler<CoreInputView*, CoreInputViewShowingEventArgs*>;

using RoGetActivationFactoryFn = HRESULT(WINAPI*)(HSTRING, REFIID, void**);
using WindowsCreateStringReferenceFn = HRESULT(WINAPI*)(PCWSTR, UINT32, HSTRING_HEADER*, HSTRING*);

struct ComBaseApi
{
    RoGetActivationFactoryFn GetActivationFactory;
    WindowsCreateStringReferenceFn CreateStringReference;
};

const ComBaseApi* ComBase()
{
    static ComBaseApi api = {};
    static const bool ready = []
    {
        const auto module = LoadLibraryExW(L"combase.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!module)
        {
            return false;
        }
        api.GetActivationFactory = reinterpret_cast<RoGetActivationFactoryFn>(
            GetProcAddress(module, "RoGetActivationFactory"));
        api.CreateStringReference = reinterpret_cast<WindowsCreateStringReferenceFn>(
            GetProcAddress(module, "WindowsCreateStringReference"));
        return api.GetActivationFactory != nullptr && api.CreateStringReference != nullptr;
    }();
    return ready ? &api : nullptr;
}

// IInputPaneInterop / IInputPane2：桌面 Win32 没有 CoreWindow 时，
// GetForCurrentView 会失败，官方全键盘仍会按「可编辑 HWND + 触摸」弹出。
constexpr GUID IidInputPaneInterop =
    {0x75CF2C57, 0x9195, 0x4931, {0x83, 0x32, 0xF0, 0xB4, 0x09, 0xE9, 0x16, 0xAF}};
constexpr GUID IidInputPane2 =
    {0x23B8D7D0, 0x5C27, 0x4466, {0x98, 0x5B, 0x7E, 0x0C, 0x85, 0xFB, 0x3D, 0x93}};

struct IInputPaneInteropRaw : IInspectable
{
    virtual HRESULT STDMETHODCALLTYPE GetForWindow(HWND window, REFIID iid, void** pane) = 0;
};

struct IInputPane2Raw : IInspectable
{
    virtual HRESULT STDMETHODCALLTYPE TryShow(boolean* result) = 0;
    virtual HRESULT STDMETHODCALLTYPE TryHide(boolean* result) = 0;
};

void HideInputPaneForWindow(HWND hwnd);
bool ShouldCancelOfficialSip();

void HideOfficialPanes()
{
    HideInputPaneForWindow(GetForegroundWindow());
    HideInputPaneForWindow(FindWindowW(L"Shell_TrayWnd", nullptr));
    HideInputPaneForWindow(FindWindowW(L"Shell_SecondaryTrayWnd", nullptr));
}

void HideInputPaneForWindow(HWND hwnd)
{
    if (!hwnd)
    {
        hwnd = GetForegroundWindow();
    }
    if (!hwnd)
    {
        return;
    }

    const auto* api = ComBase();
    if (!api)
    {
        return;
    }

    const wchar_t className[] = L"Windows.UI.ViewManagement.InputPane";
    HSTRING_HEADER header = {};
    HSTRING name = nullptr;
    if (FAILED(api->CreateStringReference(className, ARRAYSIZE(className) - 1, &header, &name)))
    {
        return;
    }

    IInputPaneInteropRaw* interop = nullptr;
    auto iid = IidInputPaneInterop;
    if (FAILED(api->GetActivationFactory(name, iid, reinterpret_cast<void**>(&interop))) || !interop)
    {
        return;
    }

    IInputPane2Raw* pane = nullptr;
    auto paneIid = IidInputPane2;
    if (SUCCEEDED(interop->GetForWindow(hwnd, paneIid, reinterpret_cast<void**>(&pane))) && pane)
    {
        boolean hidden = false;
        pane->TryHide(&hidden);
        pane->Release();
    }
    interop->Release();
}

class Canceller final : public ShowingHandler, public IAgileObject
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv)
        {
            return E_POINTER;
        }
        if (riid == IID_IUnknown || riid == __uuidof(ShowingHandler))
        {
            *ppv = static_cast<ShowingHandler*>(this);
        }
        else if (riid == __uuidof(IAgileObject))
        {
            *ppv = static_cast<IAgileObject*>(this);
        }
        else
        {
            *ppv = nullptr;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_ref); }

    STDMETHODIMP_(ULONG) Release() override
    {
        const auto remaining = InterlockedDecrement(&_ref);
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    STDMETHODIMP Invoke(ICoreInputView* sender, ICoreInputViewShowingEventArgs* args) override
    {
        if (!ShouldCancelOfficialSip())
        {
            return S_OK;
        }

        if (args)
        {
            boolean cancelled = false;
            args->TryCancel(&cancelled);
        }

        // WinUI 方案：TryCancel 之后再 TryHide，避免失焦瞬间仍闪出全键盘。
        if (sender)
        {
            ICoreInputView3* view3 = nullptr;
            if (SUCCEEDED(sender->QueryInterface(__uuidof(ICoreInputView3), reinterpret_cast<void**>(&view3)))
                && view3)
            {
                boolean hidden = false;
                view3->TryHide(&hidden);
                view3->Release();
            }
        }

        HideOfficialPanes();
        return S_OK;
    }

private:
    LONG _ref = 1;
};

class FrameworkHandler final : public IFrameworkInputPaneHandler
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv)
        {
            return E_POINTER;
        }
        if (riid == IID_IUnknown || riid == __uuidof(IFrameworkInputPaneHandler))
        {
            *ppv = static_cast<IFrameworkInputPaneHandler*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_ref); }

    STDMETHODIMP_(ULONG) Release() override
    {
        const auto remaining = InterlockedDecrement(&_ref);
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    STDMETHODIMP Showing(RECT*, BOOL) override
    {
        if (!ShouldCancelOfficialSip())
        {
            return S_OK;
        }

        HideOfficialPanes();
        return S_OK;
    }

    STDMETHODIMP Hiding(BOOL) override { return S_OK; }

private:
    LONG _ref = 1;
};

struct Subscription
{
    ICoreInputView4* View;
    EventRegistrationToken Token;
};

thread_local Subscription g_subscription = {};
thread_local IFrameworkInputPane* g_frameworkPane = {};
thread_local DWORD g_frameworkCookie = 0;
thread_local HWND g_frameworkHwnd = nullptr;
thread_local FrameworkHandler* g_frameworkHandler = nullptr;
thread_local bool g_armed = false;

// 与 Guids.h 一致。这里不 #include Guids.h，避免 initguid 重复定义。
constexpr GUID kT9Clsid =
    {0xa7e91c20, 0x4b3d, 0x4f18, {0x9c, 0x2a, 0x1b, 0x8e, 0x6d, 0x0a, 0x10, 0x01}};
constexpr GUID kT9Profile =
    {0xa7e91c20, 0x4b3d, 0x4f18, {0x9c, 0x2a, 0x1b, 0x8e, 0x6d, 0x0a, 0x10, 0x02}};

bool IsT9CurrentKeyboard()
{
    ITfInputProcessorProfileMgr* mgr = nullptr;
    if (FAILED(CoCreateInstance(
            CLSID_TF_InputProcessorProfiles,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_ITfInputProcessorProfileMgr,
            reinterpret_cast<void**>(&mgr)))
        || !mgr)
    {
        return false;
    }

    TF_INPUTPROCESSORPROFILE profile = {};
    GUID category = GUID_TFCAT_TIP_KEYBOARD;
    const auto hr = mgr->GetActiveProfile(category, &profile);
    mgr->Release();
    if (FAILED(hr))
    {
        return false;
    }

    return profile.clsid == kT9Clsid
        && (profile.guidProfile == kT9Profile || profile.guidProfile == GUID_NULL);
}

bool ShouldCancelOfficialSip()
{
    return g_armed && IsT9CurrentKeyboard();
}

void UnbindFramework()
{
    if (g_frameworkPane && g_frameworkCookie)
    {
        g_frameworkPane->Unadvise(g_frameworkCookie);
    }
    if (g_frameworkPane)
    {
        g_frameworkPane->Release();
        g_frameworkPane = nullptr;
    }
    if (g_frameworkHandler)
    {
        g_frameworkHandler->Release();
        g_frameworkHandler = nullptr;
    }
    g_frameworkCookie = 0;
    g_frameworkHwnd = nullptr;
}

}  // namespace

bool SipCancel::Enable()
{
    if (g_subscription.View)
    {
        g_armed = true;
        return true;
    }

    const auto* api = ComBase();
    if (!api)
    {
        return false;
    }

    const wchar_t className[] = L"Windows.UI.ViewManagement.Core.CoreInputView";
    HSTRING_HEADER header = {};
    HSTRING name = nullptr;
    if (FAILED(api->CreateStringReference(className, ARRAYSIZE(className) - 1, &header, &name)))
    {
        return false;
    }

    ICoreInputViewStatics* statics = nullptr;
    if (FAILED(api->GetActivationFactory(
            name,
            __uuidof(ICoreInputViewStatics),
            reinterpret_cast<void**>(&statics)))
        || !statics)
    {
        return false;
    }

    ICoreInputView* view = nullptr;
    const auto acquired = statics->GetForCurrentView(&view);
    statics->Release();
    if (FAILED(acquired) || !view)
    {
        return false;
    }

    ICoreInputView4* view4 = nullptr;
    const auto upgraded = view->QueryInterface(
        __uuidof(ICoreInputView4),
        reinterpret_cast<void**>(&view4));
    view->Release();
    if (FAILED(upgraded) || !view4)
    {
        return false;
    }

    auto* canceller = new (std::nothrow) Canceller();
    if (!canceller)
    {
        view4->Release();
        return false;
    }

    EventRegistrationToken token = {};
    const auto subscribed = view4->add_PrimaryViewShowing(canceller, &token);
    canceller->Release();
    if (FAILED(subscribed))
    {
        view4->Release();
        return false;
    }

    g_subscription.View = view4;
    g_subscription.Token = token;
    g_armed = true;

    ICoreInputView3* view3 = nullptr;
    if (SUCCEEDED(view4->QueryInterface(__uuidof(ICoreInputView3), reinterpret_cast<void**>(&view3)))
        && view3)
    {
        boolean hidden = false;
        view3->TryHide(&hidden);
        view3->Release();
    }
    HideOfficialPanes();
    return true;
}

void SipCancel::BindHost(HWND hwnd)
{
    if (!hwnd || hwnd == g_frameworkHwnd)
    {
        if (hwnd)
        {
            HideInputPaneForWindow(hwnd);
        }
        return;
    }

    UnbindFramework();
    IFrameworkInputPane* pane = nullptr;
    if (FAILED(CoCreateInstance(
            CLSID_FrameworkInputPane,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&pane)))
        || !pane)
    {
        HideInputPaneForWindow(hwnd);
        return;
    }

    auto* handler = new (std::nothrow) FrameworkHandler();
    if (!handler)
    {
        pane->Release();
        HideInputPaneForWindow(hwnd);
        return;
    }

    DWORD cookie = 0;
    if (FAILED(pane->AdviseWithHWND(hwnd, handler, &cookie)))
    {
        handler->Release();
        pane->Release();
        HideInputPaneForWindow(hwnd);
        return;
    }

    g_frameworkPane = pane;
    g_frameworkHandler = handler;
    g_frameworkCookie = cookie;
    g_frameworkHwnd = hwnd;
    HideInputPaneForWindow(hwnd);
}

void SipCancel::Refresh()
{
    Disable();
    Enable();
}

void SipCancel::Disable()
{
    g_armed = false;
    UnbindFramework();
    if (!g_subscription.View)
    {
        return;
    }
    g_subscription.View->remove_PrimaryViewShowing(g_subscription.Token);
    g_subscription.View->Release();
    g_subscription = {};
}
