// 验证 CoreInputView.PrimaryViewShowing + TryCancel 能否在普通 Win32 进程里
// 阻止官方触摸键盘出现（T9Ime 走的就是这条路）。
//
//   SipCancelProbe.exe               手指点输入框，官方键盘应当弹出
//   SipCancelProbe.exe cancel        手指点输入框，官方键盘应当完全不出现
//   加 show 参数则启动时自行 TryShow 一次，便于无人值守比对
//
// 前置：把「何时显示触摸键盘」设为“始终”，否则接了实体键盘就本来不弹，测不出差别。

#include "../src/T9Ime/SipCancel.h"

#include <windows.h>
#include <cstdio>
#include <roapi.h>
#include <winstring.h>
#include <windows.ui.viewmanagement.h>
#include <inputpaneinterop.h>

namespace
{

HWND g_edit = nullptr;
HWND g_status = nullptr;
bool g_cancel = false;
bool g_selfShow = false;
bool g_selfTouch = false;

// 触摸键盘只认「最近一次输入来自触摸」，鼠标点击不算。合成触摸设备产生的是真实
// 的 pointer 输入，因此能如实复现平板上手指点输入框的那条路径。
bool InjectTouchTap(HWND target)
{
    RECT rect = {};
    if (!GetClientRect(target, &rect))
    {
        return false;
    }
    POINT center = {(rect.right - rect.left) / 2, (rect.bottom - rect.top) / 2};
    if (!ClientToScreen(target, &center))
    {
        return false;
    }

    const auto device = CreateSyntheticPointerDevice(PT_TOUCH, 1, POINTER_FEEDBACK_NONE);
    if (!device)
    {
        return false;
    }

    POINTER_TYPE_INFO info = {};
    info.type = PT_TOUCH;
    info.touchInfo.pointerInfo.pointerType = PT_TOUCH;
    info.touchInfo.pointerInfo.pointerId = 0;
    info.touchInfo.pointerInfo.ptPixelLocation = center;
    info.touchInfo.touchFlags = TOUCH_FLAG_NONE;
    info.touchInfo.touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE;
    info.touchInfo.rcContact = {center.x - 4, center.y - 4, center.x + 4, center.y + 4};
    info.touchInfo.pressure = 1024;

    info.touchInfo.pointerInfo.pointerFlags =
        POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
    auto ok = InjectSyntheticPointerInput(device, &info, 1) != FALSE;
    Sleep(80);

    info.touchInfo.pointerInfo.pointerFlags = POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
    InjectSyntheticPointerInput(device, &info, 1);
    Sleep(40);

    info.touchInfo.pointerInfo.pointerFlags = POINTER_FLAG_UP;
    ok = InjectSyntheticPointerInput(device, &info, 1) != FALSE && ok;

    DestroySyntheticPointerDevice(device);
    return ok;
}

// 本机没有触摸屏，系统不会自己拉起键盘。主动 TryShow 走的是同一条显示路径，
// 因此照样会先触发 PrimaryViewShowing。
bool RequestOfficialKeyboard(HWND hwnd)
{
    IInputPaneInterop* interop = nullptr;
    HSTRING_HEADER header = {};
    HSTRING name = nullptr;
    const wchar_t className[] = L"Windows.UI.ViewManagement.InputPane";
    if (FAILED(WindowsCreateStringReference(className, ARRAYSIZE(className) - 1, &header, &name)))
    {
        return false;
    }
    if (FAILED(RoGetActivationFactory(name, __uuidof(IInputPaneInterop), reinterpret_cast<void**>(&interop)))
        || !interop)
    {
        return false;
    }

    ABI::Windows::UI::ViewManagement::IInputPane2* pane = nullptr;
    const auto got = interop->GetForWindow(
        hwnd,
        __uuidof(ABI::Windows::UI::ViewManagement::IInputPane2),
        reinterpret_cast<void**>(&pane));
    interop->Release();
    if (FAILED(got) || !pane)
    {
        return false;
    }

    boolean shown = false;
    const auto asked = pane->TryShow(&shown);
    pane->Release();
    return SUCCEEDED(asked) && shown;
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        g_edit = CreateWindowW(L"edit", nullptr,
            WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
            20, 20, 520, 48, hwnd, nullptr, nullptr, nullptr);
        g_status = CreateWindowW(L"static", L"",
            WS_CHILD | WS_VISIBLE,
            20, 84, 520, 120, hwnd, nullptr, nullptr, nullptr);

        // 订阅必须在窗口已经可见、且本线程持有前台窗口之后才绑得上。
        ShowWindow(hwnd, SW_SHOW);
        SetForegroundWindow(hwnd);

        const auto subscribed = g_cancel ? SipCancel::Enable() : false;
        SetFocus(g_edit);
        const auto shown = g_selfShow ? RequestOfficialKeyboard(hwnd) : false;

        wchar_t text[320] = {};
        swprintf_s(text,
            L"取消模式：%s\r\n订阅结果：%s\r\nTryShow：%s\r\n\r\n"
            L"用手指点上面的输入框。取消模式开启时官方键盘不应出现。",
            g_cancel ? L"开" : L"关",
            g_cancel ? (subscribed ? L"成功" : L"失败") : L"未启用",
            g_selfShow ? (shown ? L"已显示" : L"被拒绝") : L"未调用");
        SetWindowTextW(g_status, text);

        wchar_t logPath[MAX_PATH] = {};
        if (GetTempPathW(ARRAYSIZE(logPath), logPath))
        {
            wcscat_s(logPath, L"sipcancel-probe.log");
            if (auto* log = _wfopen(logPath, L"a, ccs=UTF-8"))
            {
                fwprintf(log, L"cancel=%d subscribed=%d selfShow=%d shown=%d\n",
                    g_cancel ? 1 : 0, subscribed ? 1 : 0, g_selfShow ? 1 : 0, shown ? 1 : 0);
                fclose(log);
            }
        }

        if (g_selfTouch)
        {
            // 等窗口真正进入前台再注入，否则触摸落到别人身上。
            SetTimer(hwnd, 1, 1200, nullptr);
        }
        return 0;
    }
    case WM_TIMER:
        if (wParam == 1)
        {
            KillTimer(hwnd, 1);
            SetForegroundWindow(hwnd);
            const auto injected = InjectTouchTap(g_edit);

            wchar_t logPath[MAX_PATH] = {};
            if (GetTempPathW(ARRAYSIZE(logPath), logPath))
            {
                wcscat_s(logPath, L"sipcancel-probe.log");
                if (auto* log = _wfopen(logPath, L"a, ccs=UTF-8"))
                {
                    fwprintf(log, L"  touchInjected=%d\n", injected ? 1 : 0);
                    fclose(log);
                }
            }
        }
        return 0;
    case WM_DESTROY:
        SipCancel::Disable();
        PostQuitMessage(0);
        return 0;
    default:
        break;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

}  // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR commandLine, int)
{
    g_cancel = commandLine && wcsstr(commandLine, L"cancel") != nullptr;
    g_selfShow = commandLine && wcsstr(commandLine, L"show") != nullptr;
    g_selfTouch = commandLine && wcsstr(commandLine, L"touch") != nullptr;

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = instance;
    wc.lpszClassName = L"T9SipCancelProbe";
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    RegisterClassW(&wc);

    const auto hwnd = CreateWindowW(wc.lpszClassName,
        g_cancel ? L"SIP 探针 — 取消模式" : L"SIP 探针 — 对照组",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 600, 300,
        nullptr, nullptr, instance, nullptr);
    if (!hwnd)
    {
        return 1;
    }

    MSG msg = {};
    while (GetMessageW(&msg, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    CoUninitialize();
    return 0;
}
