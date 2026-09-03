#include "T9Setup.h"

#include <aclapi.h>
#include <knownfolders.h>
#include <sddl.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <tlhelp32.h>
#include <urlmon.h>
#include <vector>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "urlmon.lib")

namespace
{
    constexpr wchar_t kClsid[] = L"{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}";
    constexpr wchar_t kTip[] =
        L"0804:{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002}";

    bool EndsWith(const wchar_t* name, const wchar_t* ext)
    {
        const size_t n = wcslen(name);
        const size_t e = wcslen(ext);
        return n >= e && _wcsicmp(name + n - e, ext) == 0;
    }

    bool JunkName(const wchar_t* name)
    {
        return EndsWith(name, L".pdb")
            || EndsWith(name, L".obj")
            || EndsWith(name, L".lib")
            || EndsWith(name, L".exp")
            || EndsWith(name, L".ilk");
    }

    void CopyOne(const std::wstring& from, const std::wstring& to)
    {
        if (!CopyFileW(from.c_str(), to.c_str(), FALSE))
        {
            ThrowLast(L"复制文件失败");
        }
    }

    void CopyTree(const std::wstring& from, const std::wstring& to, bool skipImeDirs)
    {
        CreateDirectoryW(to.c_str(), nullptr);
        const std::wstring pattern = from + L"\\*";
        WIN32_FIND_DATAW fd{};
        const HANDLE find = FindFirstFileW(pattern.c_str(), &fd);
        if (find == INVALID_HANDLE_VALUE)
        {
            ThrowLast(L"读取源目录失败");
        }

        do
        {
            if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0)
            {
                continue;
            }

            if (skipImeDirs
                && (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
                && (_wcsicmp(fd.cFileName, L"x64") == 0
                    || _wcsicmp(fd.cFileName, L"x86") == 0
                    || _wcsicmp(fd.cFileName, L"arm64") == 0
                    || _wcsicmp(fd.cFileName, L"arm64x") == 0
                    || _wcsicmp(fd.cFileName, L"hosts") == 0))
            {
                continue;
            }

            if (JunkName(fd.cFileName))
            {
                continue;
            }

            const std::wstring src = from + L"\\" + fd.cFileName;
            const std::wstring dest = to + L"\\" + fd.cFileName;
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            {
                CopyTree(src, dest, false);
            }
            else
            {
                CopyOne(src, dest);
            }
        }
        while (FindNextFileW(find, &fd));
        FindClose(find);
    }

    void KillT9Pane()
    {
        const HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE)
        {
            return;
        }

        PROCESSENTRY32W pe{ sizeof(pe) };
        if (Process32FirstW(snap, &pe))
        {
            do
            {
                if (_wcsicmp(pe.szExeFile, L"T9Pane.exe") == 0)
                {
                    const HANDLE proc = OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pe.th32ProcessID);
                    if (proc)
                    {
                        TerminateProcess(proc, 0);
                        WaitForSingleObject(proc, 15000);
                        CloseHandle(proc);
                    }
                }
            }
            while (Process32NextW(snap, &pe));
        }

        CloseHandle(snap);
    }

    std::wstring NewestSignedIme(const std::wstring& dir)
    {
        const std::wstring pattern = dir + L"\\T9Ime.*.dll";
        WIN32_FIND_DATAW fd{};
        const HANDLE find = FindFirstFileW(pattern.c_str(), &fd);
        if (find == INVALID_HANDLE_VALUE)
        {
            ThrowMsg((L"缺少 " + dir + L" 下的 T9Ime DLL").c_str());
        }

        std::wstring best;
        FILETIME bestTime{};
        do
        {
            const std::wstring path = dir + L"\\" + fd.cFileName;
            if (!VerifyAuthenticode(path.c_str()))
            {
                Log(L"忽略未通过签名的 %s", path.c_str());
                continue;
            }

            if (best.empty() || CompareFileTime(&fd.ftLastWriteTime, &bestTime) > 0)
            {
                best = path;
                bestTime = fd.ftLastWriteTime;
            }
        }
        while (FindNextFileW(find, &fd));
        FindClose(find);
        if (best.empty())
        {
            ThrowMsg((L"未找到已签名的 T9Ime DLL：" + dir).c_str());
        }

        return best;
    }

    std::wstring DeployIme(const std::wstring& sourceDll, const std::wstring& destArch)
    {
        CreateDirectoryW(destArch.c_str(), nullptr);
        SYSTEMTIME st{};
        GetSystemTime(&st);
        GUID guid{};
        CoCreateGuid(&guid);
        wchar_t name[MAX_PATH]{};
        swprintf_s(
            name,
            L"T9Ime.%04u%02u%02u%02u%02u%02u%03u.%08lx.dll",
            st.wYear,
            st.wMonth,
            st.wDay,
            st.wHour,
            st.wMinute,
            st.wSecond,
            st.wMilliseconds,
            guid.Data1);
        const std::wstring dest = destArch + L"\\" + name;
        CopyOne(sourceDll, dest);
        const HANDLE file = CreateFileW(dest.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            FILETIME now{};
            GetSystemTimeAsFileTime(&now);
            SetFileTime(file, nullptr, nullptr, &now);
            CloseHandle(file);
        }

        return dest;
    }

    void SetInproc(HKEY root, REGSAM wow, const wchar_t* dll)
    {
        std::wstring path = L"Software\\Classes\\CLSID\\";
        path += kClsid;
        path += L"\\InprocServer32";
        HKEY key = nullptr;
        const LSTATUS st = RegCreateKeyExW(
            root,
            path.c_str(),
            0,
            nullptr,
            0,
            KEY_SET_VALUE | wow,
            nullptr,
            &key,
            nullptr);
        if (st != ERROR_SUCCESS)
        {
            SetLastError(st);
            ThrowLast(L"写入 InprocServer32 失败");
        }

        RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(dll), static_cast<DWORD>((wcslen(dll) + 1) * sizeof(wchar_t)));
        const wchar_t* model = L"Apartment";
        RegSetValueExW(key, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(model), 22);
        RegCloseKey(key);
    }

    std::wstring GetInproc(HKEY root, REGSAM wow)
    {
        std::wstring path = L"Software\\Classes\\CLSID\\";
        path += kClsid;
        path += L"\\InprocServer32";
        HKEY key = nullptr;
        if (RegOpenKeyExW(root, path.c_str(), 0, KEY_QUERY_VALUE | wow, &key) != ERROR_SUCCESS)
        {
            return {};
        }

        wchar_t value[MAX_PATH]{};
        DWORD size = sizeof(value);
        const LSTATUS st = RegQueryValueExW(key, nullptr, nullptr, nullptr, reinterpret_cast<BYTE*>(value), &size);
        RegCloseKey(key);
        return st == ERROR_SUCCESS ? value : L"";
    }

    void RegSvr(const wchar_t* exe, const wchar_t* dll, bool add)
    {
        wchar_t args[1024]{};
        if (add)
        {
            swprintf_s(args, L"/s \"%s\"", dll);
        }
        else
        {
            swprintf_s(args, L"/s /u \"%s\"", dll);
        }

        SHELLEXECUTEINFOW sei{ sizeof(sei) };
        sei.fMask = SEE_MASK_NOCLOSEPROCESS;
        sei.lpFile = exe;
        sei.lpParameters = args;
        sei.nShow = SW_HIDE;
        if (!ShellExecuteExW(&sei))
        {
            ThrowLast(L"无法启动 regsvr32");
        }

        WaitForSingleObject(sei.hProcess, 30000);
        DWORD code = 1;
        GetExitCodeProcess(sei.hProcess, &code);
        CloseHandle(sei.hProcess);
        if (code != 0)
        {
            wchar_t buf[256]{};
            swprintf_s(buf, L"regsvr32 退出码 %u", code);
            ThrowMsg(buf);
        }
    }

    void AddLanguageBar(bool enable)
    {
        const HMODULE dll = LoadLibraryW(L"input.dll");
        if (!dll)
        {
            return;
        }

        using Fn = BOOL(WINAPI*)(LPCWSTR, DWORD);
        const auto fn = reinterpret_cast<Fn>(GetProcAddress(dll, "InstallLayoutOrTip"));
        if (fn)
        {
            fn(kTip, enable ? 0u : 1u);
        }

        FreeLibrary(dll);
    }

    REGSAM UninstallWow()
    {
        return Native64() ? KEY_WOW64_64KEY : 0;
    }

    bool SetupIsWow64()
    {
        BOOL wow = FALSE;
        return IsWow64Process(GetCurrentProcess(), &wow) && wow;
    }

    std::wstring Regsvr32Path(bool for64BitIme)
    {
        wchar_t win[MAX_PATH]{};
        GetWindowsDirectoryW(win, MAX_PATH);
        std::wstring path = win;
        if (for64BitIme)
        {
            path += SetupIsWow64() ? L"\\sysnative\\regsvr32.exe" : L"\\System32\\regsvr32.exe";
        }
        else if (Native64())
        {
            path += L"\\SysWOW64\\regsvr32.exe";
        }
        else
        {
            path += L"\\System32\\regsvr32.exe";
        }

        return path;
    }

    std::wstring StageArm64X(
        const std::wstring& source,
        const std::wstring& dest,
        const std::wstring& imeArm,
        const std::wstring& imeX64)
    {
        const std::wstring dir = dest + L"\\arm64x";
        CreateDirectoryW(dir.c_str(), nullptr);
        CopyOne(imeArm, dir + L"\\T9Ime_arm64.dll");
        CopyOne(imeX64, dir + L"\\T9Ime_x64.dll");
        const std::wstring forwarder = NewestSignedIme(source + L"\\arm64x");
        const std::wstring destForwarder = dir + L"\\T9Ime.arm64x.dll";
        CopyOne(forwarder, destForwarder);
        return destForwarder;
    }

    void PlacePaneHost(const std::wstring& source, const std::wstring& destExe)
    {
        const wchar_t* rid = NativeArm64() ? L"win-arm64" : NativeAmd64() ? L"win-x64" : L"win-x86";
        const std::wstring host = source + L"\\hosts\\" + rid + L"\\T9Pane.exe";
        if (GetFileAttributesW(host.c_str()) != INVALID_FILE_ATTRIBUTES)
        {
            CopyOne(host, destExe);
            return;
        }

        if (NativeArm64())
        {
            ThrowMsg(L"安装包缺少 ARM64 T9Pane.exe");
        }

        if (NativeX86())
        {
            ThrowMsg(L"安装包缺少 32 位 T9Pane.exe");
        }
    }

    void DeleteUninstallKey()
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(
                HKEY_LOCAL_MACHINE,
                L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                0,
                DELETE | KEY_ENUMERATE_SUB_KEYS | KEY_QUERY_VALUE | UninstallWow(),
                &key)
            == ERROR_SUCCESS)
        {
            RegDeleteTreeW(key, L"T9PinyinTouchIME");
            RegCloseKey(key);
        }
    }

    void WriteUninstallKey(const std::wstring& setup)
    {
        HKEY key = nullptr;
        RegCreateKeyExW(
            HKEY_LOCAL_MACHINE,
            L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\T9PinyinTouchIME",
            0,
            nullptr,
            0,
            KEY_SET_VALUE | UninstallWow(),
            nullptr,
            &key,
            nullptr);
        if (!key)
        {
            return;
        }

        auto set = [&](const wchar_t* name, const wchar_t* value)
        {
            RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value), static_cast<DWORD>((wcslen(value) + 1) * sizeof(wchar_t)));
        };
        set(L"DisplayName", L"T9 拼音触屏输入法");
        set(L"DisplayVersion", L"0.1.4");
        set(L"Publisher", L"sw1313");
        set(L"InstallLocation", DestDir().c_str());
        set(L"DisplayIcon", DestExe().c_str());
        const std::wstring uninstall = L"\"" + setup + L"\" /uninstall";
        set(L"UninstallString", uninstall.c_str());
        set(L"QuietUninstallString", uninstall.c_str());
        RegCloseKey(key);
    }

    void StartViaExplorer(const std::wstring& exe)
    {
        wchar_t explorer[MAX_PATH]{};
        GetWindowsDirectoryW(explorer, MAX_PATH);
        PathAppendW(explorer, L"explorer.exe");
        wchar_t args[MAX_PATH + 8]{};
        swprintf_s(args, L"\"%s\"", exe.c_str());
        ShellExecuteW(nullptr, L"open", explorer, args, nullptr, SW_SHOWNORMAL);
    }

    std::wstring NewestInstalledIme(const std::wstring& dir)
    {
        const std::wstring pattern = dir + L"\\T9Ime.*.dll";
        WIN32_FIND_DATAW fd{};
        const HANDLE find = FindFirstFileW(pattern.c_str(), &fd);
        if (find == INVALID_HANDLE_VALUE)
        {
            return {};
        }

        std::wstring best;
        FILETIME bestTime{};
        do
        {
            if (best.empty() || CompareFileTime(&fd.ftLastWriteTime, &bestTime) > 0)
            {
                best = dir + L"\\" + fd.cFileName;
                bestTime = fd.ftLastWriteTime;
            }
        }
        while (FindNextFileW(find, &fd));
        FindClose(find);
        return best;
    }

    void TryUnreg(const wchar_t* regsvr, const std::wstring& dll)
    {
        if (dll.empty())
        {
            return;
        }

        try
        {
            RegSvr(regsvr, dll.c_str(), false);
            Log(L"已注销 %s", dll.c_str());
        }
        catch (...)
        {
            Log(L"注销失败（可忽略）%s", dll.c_str());
        }
    }

    void UnregisterInstalledImes(const std::wstring& dest)
    {
        const std::wstring reg64 = Regsvr32Path(true);
        const std::wstring reg86 = Regsvr32Path(false);
        TryUnreg(reg64.c_str(), NewestInstalledIme(dest + L"\\arm64x"));
        TryUnreg(reg64.c_str(), NewestInstalledIme(dest + L"\\arm64"));
        TryUnreg(reg64.c_str(), NewestInstalledIme(dest + L"\\x64"));
        TryUnreg(reg86.c_str(), NewestInstalledIme(dest + L"\\x86"));
    }

    void DeleteKeyTree(HKEY root, const wchar_t* parent, const wchar_t* child, REGSAM wow)
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(
                root,
                parent,
                0,
                DELETE | KEY_ENUMERATE_SUB_KEYS | KEY_QUERY_VALUE | wow,
                &key) != ERROR_SUCCESS)
        {
            return;
        }

        RegDeleteTreeW(key, child);
        RegCloseKey(key);
    }

    void DeleteStaleImeRegistration()
    {
        const REGSAM views[] = {
            Native64() ? KEY_WOW64_64KEY : static_cast<REGSAM>(0),
            Native64() ? KEY_WOW64_32KEY : static_cast<REGSAM>(0)
        };
        const HKEY roots[] = { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER };
        for (auto root : roots)
        {
            for (auto wow : views)
            {
                DeleteKeyTree(root, L"Software\\Classes\\CLSID", kClsid, wow);
                DeleteKeyTree(root, L"Software\\Microsoft\\CTF\\TIP", kClsid, wow);
            }
        }
    }

    void SweepImeDirectory(const std::wstring& dir)
    {
        const std::wstring pattern = dir + L"\\*";
        WIN32_FIND_DATAW fd{};
        const HANDLE find = FindFirstFileW(pattern.c_str(), &fd);
        if (find == INVALID_HANDLE_VALUE)
        {
            return;
        }

        do
        {
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            {
                continue;
            }

            if (_wcsnicmp(fd.cFileName, L"T9Ime", 5) != 0)
            {
                continue;
            }

            const std::wstring path = dir + L"\\" + fd.cFileName;
            if (!DeleteFileW(path.c_str()))
            {
                Log(L"旧 IME 文件占用中，稍后由新文件接替 %s", path.c_str());
            }
        }
        while (FindNextFileW(find, &fd));
        FindClose(find);
    }

    // 覆盖安装：清掉旧 COM / TSF 注册和旧 DLL。不碰 %APPDATA%\T9Pane。
    void ClearOldImeInstallation(const std::wstring& dest)
    {
        UnregisterInstalledImes(dest);
        DeleteStaleImeRegistration();
        SweepImeDirectory(dest + L"\\arm64x");
        SweepImeDirectory(dest + L"\\arm64");
        SweepImeDirectory(dest + L"\\x64");
        SweepImeDirectory(dest + L"\\x86");
    }
}

namespace
{
    constexpr DWORD kReadAndExecute = FILE_GENERIC_READ | FILE_GENERIC_EXECUTE;
    const wchar_t* kAppContainerSids[] = { L"S-1-15-2-1", L"S-1-15-2-2" };

    bool AceAllows(PACL acl, PSID sid, DWORD mask)
    {
        if (!acl)
        {
            return false;
        }

        ACL_SIZE_INFORMATION info{};
        if (!GetAclInformation(acl, &info, sizeof(info), AclSizeInformation))
        {
            return false;
        }

        for (DWORD i = 0; i < info.AceCount; ++i)
        {
            void* ace = nullptr;
            if (!GetAce(acl, i, &ace))
            {
                continue;
            }

            const auto* header = static_cast<ACE_HEADER*>(ace);
            if (header->AceType != ACCESS_ALLOWED_ACE_TYPE)
            {
                continue;
            }

            const auto* allowed = static_cast<ACCESS_ALLOWED_ACE*>(ace);
            const PSID aceSid = reinterpret_cast<PSID>(const_cast<DWORD*>(&allowed->SidStart));
            if (EqualSid(aceSid, sid) && (allowed->Mask & mask) == mask)
            {
                return true;
            }
        }

        return false;
    }
}

void GrantAppContainerReadExecute(const wchar_t* path, bool directory)
{
    PACL oldAcl = nullptr;
    PSECURITY_DESCRIPTOR sd = nullptr;
    if (GetNamedSecurityInfoW(
            path,
            SE_FILE_OBJECT,
            DACL_SECURITY_INFORMATION,
            nullptr,
            nullptr,
            &oldAcl,
            nullptr,
            &sd)
        != ERROR_SUCCESS)
    {
        ThrowLast(L"读取 ACL 失败");
    }

    PSID sids[2]{};
    if (!ConvertStringSidToSidW(kAppContainerSids[0], &sids[0])
        || !ConvertStringSidToSidW(kAppContainerSids[1], &sids[1]))
    {
        LocalFree(sids[0]);
        LocalFree(sids[1]);
        LocalFree(sd);
        ThrowLast(L"构造 AppContainer SID 失败");
    }

    EXPLICIT_ACCESS_W ea[2]{};
    const DWORD inherit = directory
        ? (CONTAINER_INHERIT_ACE | OBJECT_INHERIT_ACE)
        : NO_INHERITANCE;
    for (int i = 0; i < 2; ++i)
    {
        ea[i].grfAccessPermissions = kReadAndExecute;
        ea[i].grfAccessMode = SET_ACCESS;
        ea[i].grfInheritance = inherit;
        ea[i].Trustee.TrusteeForm = TRUSTEE_IS_SID;
        ea[i].Trustee.TrusteeType = TRUSTEE_IS_UNKNOWN;
        ea[i].Trustee.ptstrName = static_cast<LPWSTR>(sids[i]);
    }

    PACL newAcl = nullptr;
    if (SetEntriesInAclW(2, ea, oldAcl, &newAcl) != ERROR_SUCCESS)
    {
        LocalFree(sids[0]);
        LocalFree(sids[1]);
        LocalFree(sd);
        ThrowLast(L"合并 ACL 失败");
    }

    const DWORD set = SetNamedSecurityInfoW(
        const_cast<LPWSTR>(path),
        SE_FILE_OBJECT,
        DACL_SECURITY_INFORMATION | UNPROTECTED_DACL_SECURITY_INFORMATION,
        nullptr,
        nullptr,
        newAcl,
        nullptr);
    LocalFree(newAcl);
    LocalFree(sids[0]);
    LocalFree(sids[1]);
    LocalFree(sd);
    if (set != ERROR_SUCCESS)
    {
        SetLastError(set);
        ThrowLast(L"写入 ACL 失败");
    }
}

bool HasAppContainerReadExecute(const wchar_t* path)
{
    PACL acl = nullptr;
    PSECURITY_DESCRIPTOR sd = nullptr;
    if (GetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, nullptr, nullptr, &acl, nullptr, &sd)
        != ERROR_SUCCESS)
    {
        return false;
    }

    bool found = true;
    for (const wchar_t* text : kAppContainerSids)
    {
        PSID sid = nullptr;
        if (!ConvertStringSidToSidW(text, &sid) || !AceAllows(acl, sid, kReadAndExecute))
        {
            found = false;
            LocalFree(sid);
            break;
        }

        LocalFree(sid);
    }

    LPWSTR sddl = nullptr;
    if (ConvertSecurityDescriptorToStringSecurityDescriptorW(
            sd,
            SDDL_REVISION_1,
            DACL_SECURITY_INFORMATION,
            &sddl,
            nullptr)
        && sddl)
    {
        Log(L"ACL %s => %s", path, sddl);
        LocalFree(sddl);
    }

    LocalFree(sd);
    return found;
}

void PatchUiAccessManifest(const wchar_t* exe, const wchar_t* manifestPath)
{
    const HANDLE file = CreateFileW(manifestPath, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        ThrowLast(L"打不开 uiAccess 清单");
    }

    const DWORD size = GetFileSize(file, nullptr);
    std::vector<BYTE> xml(size);
    DWORD read = 0;
    ReadFile(file, xml.data(), size, &read, nullptr);
    CloseHandle(file);

    const HANDLE update = BeginUpdateResourceW(exe, FALSE);
    if (!update)
    {
        ThrowLast(L"BeginUpdateResource 失败");
    }

    if (!UpdateResourceW(update, MAKEINTRESOURCEW(24), MAKEINTRESOURCEW(1), 0, xml.data(), size))
    {
        EndUpdateResourceW(update, TRUE);
        ThrowLast(L"UpdateResource 失败");
    }

    if (!EndUpdateResourceW(update, FALSE))
    {
        ThrowLast(L"EndUpdateResource 失败");
    }
}

void InstallFromSource(HWND dlg, const std::wstring& source)
{
    if (!NativeAmd64() && !NativeX86() && !NativeArm64())
    {
        ThrowMsg(L"只支持 32 位、64 位或 ARM64 Windows");
    }

    const bool native64 = Native64();
    UiStatus(dlg, L"正在停止旧进程…");
    KillT9Pane();

    const std::wstring dest = DestDir();
    const std::wstring exe = DestExe();
    UiStatus(dlg, L"正在清除旧输入法注册…");
    ClearOldImeInstallation(dest);
    CreateDirectoryW(dest.c_str(), nullptr);

    UiStatus(dlg, L"正在复制程序文件…");
    CopyTree(source, dest, true);
    PlacePaneHost(source, exe);

    wchar_t cer[MAX_PATH]{};
    PathCombineW(cer, dest.c_str(), L"T9Ime-Development.cer");
    if (GetFileAttributesW(cer) == INVALID_FILE_ATTRIBUTES)
    {
        ThrowMsg(L"缺少 T9Ime-Development.cer");
    }

    UiStatus(dlg, L"正在导入输入法证书…");
    ImportCertificateFile(cer);

    std::wstring imeNative;
    std::wstring imeArm;
    std::wstring imeX64;
    std::wstring ime86;
    UiStatus(
        dlg,
        NativeArm64() ? L"正在部署 ARM64 / x64 / x86 输入法 DLL…"
            : native64 ? L"正在部署 x64 / x86 输入法 DLL…"
                       : L"正在部署 32 位输入法 DLL…");
    if (NativeArm64())
    {
        imeArm = DeployIme(NewestSignedIme(source + L"\\arm64"), dest + L"\\arm64");
        imeX64 = DeployIme(NewestSignedIme(source + L"\\x64"), dest + L"\\x64");
        imeNative = StageArm64X(source, dest, imeArm, imeX64);
    }
    else if (NativeAmd64())
    {
        imeNative = DeployIme(NewestSignedIme(source + L"\\x64"), dest + L"\\x64");
    }

    ime86 = DeployIme(NewestSignedIme(source + L"\\x86"), dest + L"\\x86");

    GrantAppContainerReadExecute(dest.c_str(), true);
    GrantAppContainerReadExecute(exe.c_str(), false);
    if (NativeArm64())
    {
        GrantAppContainerReadExecute((dest + L"\\arm64").c_str(), true);
        GrantAppContainerReadExecute((dest + L"\\x64").c_str(), true);
        GrantAppContainerReadExecute((dest + L"\\arm64x").c_str(), true);
        GrantAppContainerReadExecute(imeArm.c_str(), false);
        GrantAppContainerReadExecute(imeX64.c_str(), false);
        GrantAppContainerReadExecute((dest + L"\\arm64x\\T9Ime_arm64.dll").c_str(), false);
        GrantAppContainerReadExecute((dest + L"\\arm64x\\T9Ime_x64.dll").c_str(), false);
        GrantAppContainerReadExecute(imeNative.c_str(), false);
    }
    else if (NativeAmd64())
    {
        GrantAppContainerReadExecute((dest + L"\\x64").c_str(), true);
        GrantAppContainerReadExecute(imeNative.c_str(), false);
    }

    GrantAppContainerReadExecute((dest + L"\\x86").c_str(), true);
    GrantAppContainerReadExecute(ime86.c_str(), false);

    wchar_t manifest[MAX_PATH]{};
    PathCombineW(manifest, source.c_str(), L"app.uia.manifest");
    UiStatus(dlg, L"正在写入 uiAccess 清单并签名…");
    PatchUiAccessManifest(exe.c_str(), manifest);
    EnsureLocalCodeSigningCert();
    SignPeFile(exe.c_str());
    GrantAppContainerReadExecute(exe.c_str(), false);
    if (!VerifyAuthenticode(exe.c_str()))
    {
        ThrowMsg(L"T9Pane.exe 签名校验失败");
    }

    if (!HasAppContainerReadExecute(exe.c_str()))
    {
        GrantAppContainerReadExecute(exe.c_str(), false);
        if (!HasAppContainerReadExecute(exe.c_str()))
        {
            ThrowMsg(L"T9Pane.exe 缺少 AppContainer ACL");
        }
    }

    const std::wstring reg64 = Regsvr32Path(true);
    const std::wstring reg86 = Regsvr32Path(false);
    const REGSAM wow64 = native64 ? KEY_WOW64_64KEY : 0;
    const REGSAM wow32 = native64 ? KEY_WOW64_32KEY : 0;

    UiStatus(dlg, native64 ? L"正在注册输入法（含 32 位程序）…" : L"正在注册 32 位输入法…");
    if (native64)
    {
        RegSvr(reg64.c_str(), imeNative.c_str(), true);
        SetInproc(HKEY_LOCAL_MACHINE, wow64, imeNative.c_str());
        SetInproc(HKEY_CURRENT_USER, wow64, imeNative.c_str());
    }

    RegSvr(reg86.c_str(), ime86.c_str(), true);
    SetInproc(HKEY_CURRENT_USER, wow32, ime86.c_str());

    if (native64)
    {
        if (_wcsicmp(GetInproc(HKEY_LOCAL_MACHINE, wow64).c_str(), imeNative.c_str()) != 0
            || _wcsicmp(GetInproc(HKEY_CURRENT_USER, wow64).c_str(), imeNative.c_str()) != 0
            || _wcsicmp(GetInproc(HKEY_LOCAL_MACHINE, wow32).c_str(), ime86.c_str()) != 0
            || _wcsicmp(GetInproc(HKEY_CURRENT_USER, wow32).c_str(), ime86.c_str()) != 0)
        {
            ThrowMsg(L"InprocServer32 校验失败");
        }

        if (!VerifyAuthenticode(imeNative.c_str()) || !VerifyAuthenticode(ime86.c_str()))
        {
            ThrowMsg(L"已部署的 T9Ime DLL 签名无效");
        }
    }
    else if (_wcsicmp(GetInproc(HKEY_LOCAL_MACHINE, 0).c_str(), ime86.c_str()) != 0
        || _wcsicmp(GetInproc(HKEY_CURRENT_USER, 0).c_str(), ime86.c_str()) != 0
        || !VerifyAuthenticode(ime86.c_str()))
    {
        ThrowMsg(L"InprocServer32 或签名校验失败");
    }

    AddLanguageBar(true);

    wchar_t self[MAX_PATH]{};
    GetModuleFileNameW(nullptr, self, MAX_PATH);
    const std::wstring setupDest = dest + L"\\T9Setup.exe";
    if (_wcsicmp(self, setupDest.c_str()) != 0)
    {
        CopyFileW(self, setupDest.c_str(), FALSE);
    }

    const std::wstring uninstallDest = dest + L"\\Uninstall.exe";
    CopyFileW(self, uninstallDest.c_str(), FALSE);
    WriteUninstallKey(uninstallDest);
    UiStatus(dlg, L"正在启动键盘后端…");
    StartViaExplorer(exe);
    Log(L"安装完成 dest=%s amd64=%d arm64=%d", dest.c_str(), NativeAmd64() ? 1 : 0, NativeArm64() ? 1 : 0);
}

    void RestoreOfficialTouchKeyboard()
    {
        HKEY key = nullptr;
        const REGSAM access = KEY_READ | KEY_WRITE | KEY_WOW64_64KEY;
        if (RegOpenKeyExW(
                HKEY_CURRENT_USER,
                L"Software\\Microsoft\\TabletTip\\1.7",
                0,
                access,
                &key) != ERROR_SUCCESS)
        {
            return;
        }

        DWORD type = 0;
        DWORD held = 0;
        DWORD size = sizeof(held);
        if (RegQueryValueExW(key, L"T9Pane.Backup.Active", nullptr, &type, reinterpret_cast<BYTE*>(&held), &size)
                != ERROR_SUCCESS
            || held == 0)
        {
            RegCloseKey(key);
            return;
        }

        auto restoreDword = [&](const wchar_t* name, const wchar_t* hadName, const wchar_t* backupName)
        {
            DWORD had = 0;
            DWORD hadSize = sizeof(had);
            if (RegQueryValueExW(key, hadName, nullptr, &type, reinterpret_cast<BYTE*>(&had), &hadSize) != ERROR_SUCCESS)
            {
                return;
            }

            if (had)
            {
                DWORD value = 0;
                DWORD valueSize = sizeof(value);
                if (RegQueryValueExW(
                        key,
                        backupName,
                        nullptr,
                        &type,
                        reinterpret_cast<BYTE*>(&value),
                        &valueSize) == ERROR_SUCCESS)
                {
                    RegSetValueExW(
                        key,
                        name,
                        0,
                        REG_DWORD,
                        reinterpret_cast<const BYTE*>(&value),
                        sizeof(value));
                }

                return;
            }

            RegDeleteValueW(key, name);
        };

        restoreDword(
            L"TouchKeyboardTapInvoke",
            L"T9Pane.Backup.HadTouchKeyboardTapInvoke",
            L"T9Pane.Backup.TouchKeyboardTapInvoke");
        restoreDword(
            L"EnableDesktopModeAutoInvoke",
            L"T9Pane.Backup.HadEnableDesktopModeAutoInvoke",
            L"T9Pane.Backup.EnableDesktopModeAutoInvoke");
        RegDeleteValueW(key, L"T9Pane.Backup.Active");
        RegDeleteValueW(key, L"T9Pane.Backup.HadEnableDesktopModeAutoInvoke");
        RegDeleteValueW(key, L"T9Pane.Backup.EnableDesktopModeAutoInvoke");
        RegDeleteValueW(key, L"T9Pane.Backup.HadTouchKeyboardTapInvoke");
        RegDeleteValueW(key, L"T9Pane.Backup.TouchKeyboardTapInvoke");
        RegCloseKey(key);
        DWORD_PTR result = 0;
        SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            0,
            0,
            SMTO_ABORTIFHUNG,
            800,
            &result);
        Log(L"已恢复系统触摸键盘「显示触摸键盘」原设置");
    }

void UninstallProduct(HWND dlg)
{
    UiStatus(dlg, L"正在卸载…");
    KillT9Pane();
    RestoreOfficialTouchKeyboard();
    const std::wstring dest = DestDir();
    UnregisterInstalledImes(dest);
    DeleteStaleImeRegistration();
    AddLanguageBar(false);
    SHFILEOPSTRUCTW op{};
    wchar_t from[MAX_PATH + 2]{};
    wcsncpy_s(from, dest.c_str(), MAX_PATH);
    op.wFunc = FO_DELETE;
    op.pFrom = from;
    op.fFlags = FOF_NO_UI | FOF_NOCONFIRMATION | FOF_SILENT;
    SHFileOperationW(&op);
    DeleteUninstallKey();
    Log(L"卸载完成");
}

bool EnsureDotNetRuntime(HWND dlg)
{
    const bool native64 = Native64();
    std::wstring root = native64
        ? KnownFolderPath(FOLDERID_ProgramFilesX64)
        : KnownFolderPath(FOLDERID_ProgramFiles);
    if (root.empty())
    {
        wchar_t fallback[MAX_PATH]{};
        SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILES, nullptr, SHGFP_TYPE_CURRENT, fallback);
        root = fallback;
    }

    wchar_t dir[MAX_PATH]{};
    PathCombineW(dir, root.c_str(), L"dotnet\\shared\\Microsoft.WindowsDesktop.App");
    const std::wstring pattern = std::wstring(dir) + L"\\8.*";
    WIN32_FIND_DATAW fd{};
    const HANDLE find = FindFirstFileW(pattern.c_str(), &fd);
    if (find != INVALID_HANDLE_VALUE)
    {
        FindClose(find);
        return true;
    }

    const wchar_t* rid = NativeArm64() ? L"win-arm64" : native64 ? L"win-x64" : L"win-x86";
    wchar_t url[160]{};
    swprintf_s(url, L"https://aka.ms/dotnet/8.0/windowsdesktop-runtime-%s.exe", rid);
    UiStatus(dlg, L"未检测到 .NET 8 桌面运行时，正在下载…");
    wchar_t tmp[MAX_PATH]{};
    GetTempPathW(MAX_PATH, tmp);
    wchar_t name[64]{};
    swprintf_s(name, L"windowsdesktop-runtime-8.0-%s.exe", rid);
    PathAppendW(tmp, name);
    HRESULT hr = URLDownloadToFileW(
        nullptr,
        url,
        tmp,
        0,
        nullptr);
    if (FAILED(hr))
    {
        Log(L"下载运行时失败 hr=0x%08X", static_cast<unsigned>(hr));
        return false;
    }

    UiStatus(dlg, L"正在安装 .NET 8 桌面运行时…");
    SHELLEXECUTEINFOW sei{ sizeof(sei) };
    sei.fMask = SEE_MASK_NOCLOSEPROCESS;
    sei.lpFile = tmp;
    sei.lpParameters = L"/install /quiet /norestart";
    sei.nShow = SW_HIDE;
    if (!ShellExecuteExW(&sei))
    {
        return false;
    }

    WaitForSingleObject(sei.hProcess, 300000);
    DWORD code = 1;
    GetExitCodeProcess(sei.hProcess, &code);
    CloseHandle(sei.hProcess);
    Log(L".NET 运行时安装退出码 %u", code);
    return code == 0 || code == 3010;
}
