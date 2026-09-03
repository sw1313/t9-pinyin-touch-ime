#pragma once

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif

#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0A00

#include <windows.h>
#include <shellapi.h>
#include <objbase.h>
#include <string>

struct SetupError
{
    std::wstring message;
};

void Log(const wchar_t* format, ...);
void UiStatus(HWND dlg, const wchar_t* text);
[[noreturn]] void ThrowLast(const wchar_t* what);
[[noreturn]] void ThrowMsg(const wchar_t* what);

bool NativeAmd64();
bool NativeX86();
std::wstring DestDir();
std::wstring DestExe();
std::wstring LogPath();
std::wstring KnownFolderPath(const GUID& folder);

void InstallFromSource(HWND dlg, const std::wstring& source);
void UninstallProduct(HWND dlg);

bool HasArg(const wchar_t* cmd, const wchar_t* flag);
bool ExtractEmbeddedPayload(const std::wstring& zipPath);
bool SidecarSource(std::wstring& source);
bool EnsureDotNetRuntime(HWND dlg);
bool VerifyAuthenticode(const wchar_t* path);
void SignPeFile(const wchar_t* path);
void ImportCertificateFile(const wchar_t* cerPath);
void EnsureLocalCodeSigningCert();
void GrantAppContainerReadExecute(const wchar_t* path, bool directory);
bool HasAppContainerReadExecute(const wchar_t* path);
void PatchUiAccessManifest(const wchar_t* exe, const wchar_t* manifestPath);

extern HWND g_dlg;
