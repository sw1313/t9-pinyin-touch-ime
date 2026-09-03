#include "T9Setup.h"
#include "resource.h"

#include <commctrl.h>
#include <cstdarg>
#include <cwctype>
#include <shlobj.h>
#include <shlwapi.h>
#include <string>

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "user32.lib")

HWND g_dlg = nullptr;

namespace
{
    HANDLE g_log = INVALID_HANDLE_VALUE;
    bool g_quiet = false;
    bool g_uninstall = false;

    std::wstring ModuleDir()
    {
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        PathRemoveFileSpecW(path);
        return path;
    }

    void OpenLog()
    {
        CreateDirectoryW(LogPath().c_str(), nullptr);
        wchar_t file[MAX_PATH]{};
        PathCombineW(file, LogPath().c_str(), L"install-uiaccess.log");
        g_log = CreateFileW(
            file,
            FILE_APPEND_DATA,
            FILE_SHARE_READ,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
    }
}

void Log(const wchar_t* format, ...)
{
    wchar_t line[2048]{};
    SYSTEMTIME st{};
    GetLocalTime(&st);
    const int prefix = swprintf_s(
        line,
        L"%04u-%02u-%02u %02u:%02u:%02u.%03u ",
        st.wYear,
        st.wMonth,
        st.wDay,
        st.wHour,
        st.wMinute,
        st.wSecond,
        st.wMilliseconds);
    va_list args;
    va_start(args, format);
    vswprintf_s(line + prefix, _countof(line) - prefix - 3, format, args);
    va_end(args);
    wcscat_s(line, L"\r\n");
    if (g_log != INVALID_HANDLE_VALUE)
    {
        const int bytes = WideCharToMultiByte(CP_UTF8, 0, line, -1, nullptr, 0, nullptr, nullptr);
        std::string utf8(bytes > 1 ? static_cast<size_t>(bytes - 1) : 0, '\0');
        if (!utf8.empty())
        {
            WideCharToMultiByte(CP_UTF8, 0, line, -1, &utf8[0], bytes, nullptr, nullptr);
            DWORD written = 0;
            WriteFile(g_log, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
        }
    }
}

void UiStatus(HWND dlg, const wchar_t* text)
{
    Log(L"%s", text);
    if (dlg && IsWindow(dlg))
    {
        SetDlgItemTextW(dlg, IDC_STATUS, text);
    }
}

[[noreturn]] void ThrowLast(const wchar_t* what)
{
    wchar_t buf[640]{};
    swprintf_s(buf, L"%s（错误 %u）", what, GetLastError());
    Log(L"%s", buf);
    throw SetupError{ buf };
}

[[noreturn]] void ThrowMsg(const wchar_t* what)
{
    Log(L"%s", what);
    throw SetupError{ what };
}

std::wstring DestDir()
{
    wchar_t root[MAX_PATH]{};
    SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILES, nullptr, SHGFP_TYPE_CURRENT, root);
    wchar_t dest[MAX_PATH]{};
    PathCombineW(dest, root, L"T9Pane");
    return dest;
}

std::wstring DestExe()
{
    wchar_t exe[MAX_PATH]{};
    PathCombineW(exe, DestDir().c_str(), L"T9Pane.exe");
    return exe;
}

std::wstring LogPath()
{
    wchar_t appdata[MAX_PATH]{};
    SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, appdata);
    wchar_t dir[MAX_PATH]{};
    PathCombineW(dir, appdata, L"T9Pane");
    return dir;
}

bool HasArg(const wchar_t* cmd, const wchar_t* flag)
{
    if (!cmd || !flag)
    {
        return false;
    }

    const std::wstring hay = cmd;
    std::wstring needle = flag;
    auto lower = [](std::wstring value)
    {
        for (auto& ch : value)
        {
            ch = static_cast<wchar_t>(towlower(ch));
        }
        return value;
    };
    return lower(hay).find(lower(needle)) != std::wstring::npos;
}

bool ExtractEmbeddedPayload(const std::wstring& zipPath)
{
    const HRSRC res = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_PAYLOAD), RT_RCDATA);
    if (!res)
    {
        return false;
    }

    const DWORD size = SizeofResource(nullptr, res);
    if (size < 64)
    {
        return false;
    }

    const HGLOBAL handle = LoadResource(nullptr, res);
    const void* data = LockResource(handle);
    if (!data)
    {
        return false;
    }

    const HANDLE file = CreateFileW(
        zipPath.c_str(),
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        ThrowLast(L"无法写出安装包");
    }

    DWORD written = 0;
    const BOOL ok = WriteFile(file, data, size, &written, nullptr);
    CloseHandle(file);
    if (!ok || written != size)
    {
        ThrowLast(L"写入安装包失败");
    }

    Log(L"已释放内嵌安装包 %u 字节", size);
    return true;
}

bool SidecarSource(std::wstring& source)
{
    wchar_t exe[MAX_PATH]{};
    PathCombineW(exe, ModuleDir().c_str(), L"T9Pane.exe");
    if (GetFileAttributesW(exe) == INVALID_FILE_ATTRIBUTES)
    {
        return false;
    }

    source = ModuleDir();
    return true;
}

bool ExpandZip(const std::wstring& zip, const std::wstring& dest)
{
    CreateDirectoryW(dest.c_str(), nullptr);
    wchar_t tar[MAX_PATH]{};
    GetSystemDirectoryW(tar, MAX_PATH);
    PathAppendW(tar, L"tar.exe");
    if (GetFileAttributesW(tar) == INVALID_FILE_ATTRIBUTES)
    {
        ThrowMsg(L"系统缺少 tar.exe，无法解开安装包");
    }

    wchar_t args[2048]{};
    swprintf_s(args, L"\"%s\" -xf \"%s\" -C \"%s\"", tar, zip.c_str(), dest.c_str());
    STARTUPINFOW si{ sizeof(si) };
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION pi{};
    std::wstring command = args;
    if (!CreateProcessW(nullptr, &command[0], nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
    {
        ThrowLast(L"无法启动 tar.exe");
    }

    WaitForSingleObject(pi.hProcess, 120000);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    if (code != 0)
    {
        ThrowMsg(L"解开安装包失败");
    }

    wchar_t probe[MAX_PATH]{};
    PathCombineW(probe, dest.c_str(), L"T9Pane.exe");
    if (GetFileAttributesW(probe) == INVALID_FILE_ATTRIBUTES)
    {
        ThrowMsg(L"安装包里没有 T9Pane.exe");
    }

    return true;
}

std::wstring ResolveSource(HWND dlg)
{
    wchar_t temp[MAX_PATH]{};
    GetTempPathW(MAX_PATH, temp);
    wchar_t zip[MAX_PATH]{};
    PathCombineW(zip, temp, L"t9setup-payload.zip");
    wchar_t extract[MAX_PATH]{};
    PathCombineW(extract, temp, L"t9setup-extract");

    if (ExtractEmbeddedPayload(zip))
    {
        UiStatus(dlg, L"正在解开安装包…");
        SHFILEOPSTRUCTW op{};
        wchar_t from[MAX_PATH + 2]{};
        wcsncpy_s(from, extract, MAX_PATH);
        op.wFunc = FO_DELETE;
        op.pFrom = from;
        op.fFlags = FOF_NO_UI | FOF_NOCONFIRMATION | FOF_SILENT;
        SHFileOperationW(&op);
        ExpandZip(zip, extract);
        return extract;
    }

    std::wstring sidecar;
    if (SidecarSource(sidecar))
    {
        Log(L"使用旁路目录 %s", sidecar.c_str());
        return sidecar;
    }

    ThrowMsg(L"安装程序里没有程序文件。请重新下载 Setup。");
}

DWORD WINAPI Worker(void*)
{
    int exitCode = 0;
    std::wstring error;
    try
    {
        if (g_uninstall)
        {
            UninstallProduct(g_dlg);
        }
        else
        {
            if (!EnsureDotNetRuntime(g_dlg))
            {
                ThrowMsg(L"需要 .NET 8 桌面运行时。安装程序下载失败，请先手动安装后再运行 Setup。");
            }

            const std::wstring source = ResolveSource(g_dlg);
            InstallFromSource(g_dlg, source);
        }
    }
    catch (const SetupError& ex)
    {
        error = ex.message;
        exitCode = 1;
    }
    catch (...)
    {
        error = L"未知错误";
        exitCode = 1;
    }

    if (g_dlg)
    {
        auto* text = new std::wstring(error);
        PostMessageW(g_dlg, WM_APP + 1, exitCode, reinterpret_cast<LPARAM>(text));
    }

    return exitCode;
}

INT_PTR CALLBACK DialogProc(HWND dlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG:
        g_dlg = dlg;
        SetWindowTextW(dlg, L"T9 拼音触屏输入法");
        SetDlgItemTextW(dlg, IDC_STATUS, g_uninstall ? L"正在卸载…" : L"正在安装…");
        {
            const HWND bar = GetDlgItem(dlg, IDC_PROGRESS);
            SetWindowLongW(bar, GWL_STYLE, GetWindowLongW(bar, GWL_STYLE) | PBS_MARQUEE);
            SendMessageW(bar, PBM_SETMARQUEE, TRUE, 30);
        }
        CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr);
        return TRUE;
    case WM_APP + 1:
    {
        const HWND bar = GetDlgItem(dlg, IDC_PROGRESS);
        SendMessageW(bar, PBM_SETMARQUEE, FALSE, 0);
        SetWindowLongW(bar, GWL_STYLE, (GetWindowLongW(bar, GWL_STYLE) & ~PBS_MARQUEE) | PBS_SMOOTH);
        SendMessageW(bar, PBM_SETRANGE32, 0, 100);
        SendMessageW(bar, PBM_SETPOS, wParam == 0 ? 100 : 0, 0);
        EnableWindow(GetDlgItem(dlg, IDC_CLOSE), TRUE);
        auto* error = reinterpret_cast<std::wstring*>(lParam);
        if (wParam == 0)
        {
            SetDlgItemTextW(
                dlg,
                IDC_STATUS,
                g_uninstall
                    ? L"卸载完成。"
                    : L"安装完成。按 Win+空格 切到「T9 九键」，再点一下输入框。");
            SetTimer(dlg, 1, 1200, nullptr);
        }
        else if (error)
        {
            SetDlgItemTextW(dlg, IDC_STATUS, error->c_str());
            if (!g_quiet)
            {
                MessageBoxW(dlg, error->c_str(), L"T9 拼音触屏输入法", MB_ICONERROR);
            }
            EndDialog(dlg, static_cast<INT_PTR>(wParam));
        }
        delete error;
        if (g_quiet)
        {
            EndDialog(dlg, static_cast<INT_PTR>(wParam));
        }
        return TRUE;
    }
    case WM_TIMER:
        if (wParam == 1)
        {
            KillTimer(dlg, 1);
            EndDialog(dlg, 0);
            return TRUE;
        }
        break;
    case WM_COMMAND:
        if (LOWORD(wParam) == IDC_CLOSE || LOWORD(wParam) == IDCANCEL)
        {
            EndDialog(dlg, 0);
            return TRUE;
        }
        break;
    case WM_CLOSE:
        EndDialog(dlg, 0);
        return TRUE;
    default:
        break;
    }

    return FALSE;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR cmd, int)
{
    BOOL wow = FALSE;
    IsWow64Process(GetCurrentProcess(), &wow);
    if (wow)
    {
        MessageBoxW(nullptr, L"请运行 64 位安装程序。", L"T9 拼音触屏输入法", MB_ICONERROR);
        return 1;
    }

    g_quiet = HasArg(cmd, L"/quiet") || HasArg(cmd, L"/q");
    wchar_t module[MAX_PATH]{};
    GetModuleFileNameW(nullptr, module, MAX_PATH);
    const wchar_t* fileName = PathFindFileNameW(module);
    g_uninstall = HasArg(cmd, L"/uninstall")
        || (fileName && _wcsicmp(fileName, L"Uninstall.exe") == 0);

    OpenLog();
    Log(L"T9Setup 启动 uninstall=%d quiet=%d", g_uninstall ? 1 : 0, g_quiet ? 1 : 0);

    INITCOMMONCONTROLSEX icc{ sizeof(icc), ICC_PROGRESS_CLASS };
    InitCommonControlsEx(&icc);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    if (g_quiet)
    {
        g_dlg = nullptr;
        const DWORD code = Worker(nullptr);
        CoUninitialize();
        return static_cast<int>(code);
    }

    const INT_PTR result = DialogBoxParamW(instance, MAKEINTRESOURCEW(IDD_MAIN), nullptr, DialogProc, 0);
    CoUninitialize();
    return result < 0 ? 1 : 0;
}
