#include <initguid.h>
#include "T9Ime.h"
#include "Guids.h"
#include <msctf.h>
#include <string>

extern "C" IMAGE_DOS_HEADER __ImageBase;

static HRESULT SetKeyValue(HKEY root, const wchar_t* path, const wchar_t* name, const wchar_t* value)
{
    HKEY key = nullptr;
    const auto err = RegCreateKeyExW(root, path, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (err != ERROR_SUCCESS)
    {
        return HRESULT_FROM_WIN32(err);
    }

    const auto hr = HRESULT_FROM_WIN32(RegSetValueExW(
        key, name, 0, REG_SZ,
        reinterpret_cast<const BYTE*>(value),
        static_cast<DWORD>((wcslen(value) + 1) * sizeof(wchar_t))));
    RegCloseKey(key);
    return hr;
}

static std::wstring ModulePath()
{
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), path, MAX_PATH);
    return path;
}

static HRESULT RegisterCategories(BOOL enable)
{
    ITfCategoryMgr* cat = nullptr;
    auto hr = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
        IID_ITfCategoryMgr, reinterpret_cast<void**>(&cat));
    if (FAILED(hr) || !cat)
    {
        return hr;
    }

    const GUID* cats[] = {
        &GUID_TFCAT_TIP_KEYBOARD,
        &GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT,
        &GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT,
    };

    if (enable)
    {
        cat->UnregisterCategory(
            CLSID_T9Ime,
            GUID_TFCAT_TIPCAP_UIELEMENTENABLED,
            CLSID_T9Ime);
    }

    for (auto* guid : cats)
    {
        if (enable)
        {
            cat->RegisterCategory(CLSID_T9Ime, *guid, CLSID_T9Ime);
        }
        else
        {
            cat->UnregisterCategory(CLSID_T9Ime, *guid, CLSID_T9Ime);
        }
    }

    cat->Release();
    return S_OK;
}

HRESULT RegisterT9Ime()
{
    wchar_t clsid[64] = {};
    StringFromGUID2(CLSID_T9Ime, clsid, 64);

    const auto dll = ModulePath();
    std::wstring clsidKey = L"CLSID\\";
    clsidKey += clsid;
    const auto machineClsidKey = L"Software\\Classes\\" + clsidKey;
    auto hr = SetKeyValue(HKEY_LOCAL_MACHINE, machineClsidKey.c_str(), nullptr, T9IME_DESC);
    if (FAILED(hr))
    {
        return hr;
    }
    hr = SetKeyValue(
        HKEY_LOCAL_MACHINE,
        (machineClsidKey + L"\\InprocServer32").c_str(),
        nullptr,
        dll.c_str());
    if (FAILED(hr))
    {
        return hr;
    }
    hr = SetKeyValue(
        HKEY_LOCAL_MACHINE,
        (machineClsidKey + L"\\InprocServer32").c_str(),
        L"ThreadingModel",
        L"Apartment");
    if (FAILED(hr))
    {
        return hr;
    }

    const auto slash = dll.find_last_of(L"\\/");
    const auto dir = slash == std::wstring::npos ? dll : dll.substr(0, slash);
    hr = SetKeyValue(HKEY_CURRENT_USER, T9IME_REG_ROOT, L"InstallDir", dir.c_str());
    if (FAILED(hr))
    {
        return hr;
    }
    hr = SetKeyValue(HKEY_CURRENT_USER, T9IME_REG_ROOT, L"DllPath", dll.c_str());
    if (FAILED(hr))
    {
        return hr;
    }

    hr = RegisterCategories(TRUE);
    if (FAILED(hr))
    {
        return hr;
    }

    ITfInputProcessorProfiles* profiles = nullptr;
    hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfiles, reinterpret_cast<void**>(&profiles));
    if (FAILED(hr) || !profiles)
    {
        return hr;
    }

    hr = profiles->Register(CLSID_T9Ime);
    if (SUCCEEDED(hr))
    {
        profiles->AddLanguageProfile(
            CLSID_T9Ime,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            GUID_T9ImeProfile,
            T9IME_DESC,
            static_cast<ULONG>(wcslen(T9IME_DESC)),
            dll.c_str(),
            static_cast<ULONG>(dll.size()),
            static_cast<ULONG>(-1));
        profiles->EnableLanguageProfile(
            CLSID_T9Ime,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            GUID_T9ImeProfile,
            TRUE);
        // EnableLanguageProfileByDefault controls whether the profile is usable
        // by default; it does not make the profile the active/default IME.
        profiles->EnableLanguageProfileByDefault(
            CLSID_T9Ime,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            GUID_T9ImeProfile,
            TRUE);
    }

    ITfInputProcessorProfileMgr* mgr = nullptr;
    if (SUCCEEDED(profiles->QueryInterface(IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(&mgr))) && mgr)
    {
        mgr->RegisterProfile(
            CLSID_T9Ime,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            GUID_T9ImeProfile,
            T9IME_DESC,
            static_cast<ULONG>(wcslen(T9IME_DESC)),
            dll.c_str(),
            static_cast<ULONG>(dll.size()),
            static_cast<ULONG>(-1),
            nullptr,
            0,
            TRUE,
            0);
        mgr->Release();
    }

    profiles->Release();
    return SUCCEEDED(hr) ? S_OK : hr;
}

HRESULT UnregisterT9Ime()
{
    ITfInputProcessorProfiles* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfiles, reinterpret_cast<void**>(&profiles))) && profiles)
    {
        profiles->EnableLanguageProfile(
            CLSID_T9Ime,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            GUID_T9ImeProfile,
            FALSE);
        ITfInputProcessorProfileMgr* mgr = nullptr;
        if (SUCCEEDED(profiles->QueryInterface(IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(&mgr))) && mgr)
        {
            mgr->UnregisterProfile(
                CLSID_T9Ime,
                MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
                GUID_T9ImeProfile,
                0);
            mgr->Release();
        }

        profiles->Unregister(CLSID_T9Ime);
        profiles->Release();
    }

    RegisterCategories(FALSE);

    wchar_t clsid[64] = {};
    StringFromGUID2(CLSID_T9Ime, clsid, 64);
    std::wstring clsidKey = L"Software\\Classes\\CLSID\\";
    clsidKey += clsid;
    clsidKey += L"\\InprocServer32";
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, clsidKey.c_str());
    clsidKey = L"Software\\Classes\\CLSID\\";
    clsidKey += clsid;
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, clsidKey.c_str());
    return S_OK;
}

STDAPI T9ImeClearDefault()
{
    ITfInputProcessorProfiles* profiles = nullptr;
    auto hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfiles, reinterpret_cast<void**>(&profiles));
    if (FAILED(hr) || !profiles)
    {
        return hr;
    }

    hr = profiles->EnableLanguageProfile(
        CLSID_T9Ime,
        MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
        GUID_T9ImeProfile,
        TRUE);
    const auto defaultHr = profiles->EnableLanguageProfileByDefault(
        CLSID_T9Ime,
        MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
        GUID_T9ImeProfile,
        TRUE);
    profiles->Release();
    return SUCCEEDED(hr) ? hr : defaultHr;
}

STDAPI T9ImeActivate()
{
    const auto initHr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ITfInputProcessorProfileMgr* profiles = nullptr;
    auto hr = CoCreateInstance(
        CLSID_TF_InputProcessorProfiles,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfileMgr,
        reinterpret_cast<void**>(&profiles));
    if (SUCCEEDED(hr) && profiles)
    {
        hr = profiles->ActivateProfile(
            TF_PROFILETYPE_INPUTPROCESSOR,
            MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED),
            CLSID_T9Ime,
            GUID_T9ImeProfile,
            nullptr,
            TF_IPPMF_FORSESSION
                | TF_IPPMF_ENABLEPROFILE
                | TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE);
        profiles->Release();
    }
    if (SUCCEEDED(initHr))
    {
        CoUninitialize();
    }
    return hr;
}

void LaunchBackend()
{
    static volatile LONG launching = 0;
    if (InterlockedCompareExchange(&launching, 1, 0) != 0)
    {
        return;
    }

    HANDLE gate = CreateMutexW(nullptr, FALSE, L"Local\\T9Pane.BackendLaunch");
    const auto gateWait = gate ? WaitForSingleObject(gate, 1500) : WAIT_FAILED;
    if (gate && gateWait != WAIT_OBJECT_0 && gateWait != WAIT_ABANDONED)
    {
        CloseHandle(gate);
        InterlockedExchange(&launching, 0);
        return;
    }

    if (WaitNamedPipeW(T9IME_PIPE_LOCAL, 50)
        || WaitNamedPipeW(T9IME_PIPE, 50))
    {
        if (gate)
        {
            ReleaseMutex(gate);
            CloseHandle(gate);
        }
        InterlockedExchange(&launching, 0);
        return;
    }

    wchar_t dir[MAX_PATH] = {};
    DWORD size = sizeof(dir);
    if (RegGetValueW(HKEY_CURRENT_USER, T9IME_REG_ROOT, L"InstallDir", RRF_RT_REG_SZ, nullptr, dir, &size) != ERROR_SUCCESS)
    {
        if (gate)
        {
            ReleaseMutex(gate);
            CloseHandle(gate);
        }
        InterlockedExchange(&launching, 0);
        return;
    }

    std::wstring exe = dir;
    exe += L"\\T9Pane.exe";
    if (GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        if (gate)
        {
            ReleaseMutex(gate);
            CloseHandle(gate);
        }
        InterlockedExchange(&launching, 0);
        return;
    }

    ShellExecuteW(nullptr, L"open", exe.c_str(), nullptr, dir, SW_SHOWNOACTIVATE);
    for (int attempt = 0;
         attempt < 30
         && !WaitNamedPipeW(T9IME_PIPE_LOCAL, 100)
         && !WaitNamedPipeW(T9IME_PIPE, 100);
         ++attempt)
    {
        Sleep(100);
    }

    if (gate)
    {
        ReleaseMutex(gate);
        CloseHandle(gate);
    }
    InterlockedExchange(&launching, 0);
}
