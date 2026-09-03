#include "T9Setup.h"

#include <wincrypt.h>
#include <wintrust.h>
#include <softpub.h>
#include <vector>

#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "wintrust.lib")

namespace
{
    constexpr DWORD kSignerSubjectFile = 1;
    constexpr DWORD kSignerCertStore = 2;
    constexpr DWORD kSignerCertPolicyChain = 2;
    constexpr DWORD kSignerAuthcodeAttr = 1;
    constexpr ALG_ID kCalgSha256 = 0x0000800c;

    struct SIGNER_FILE_INFO_X
    {
        DWORD cbSize;
        LPCWSTR pwszFileName;
        HANDLE hFile;
    };

    struct SIGNER_SUBJECT_INFO_X
    {
        DWORD cbSize;
        DWORD* pdwIndex;
        DWORD dwSubjectChoice;
        SIGNER_FILE_INFO_X* pSignerFileInfo;
    };

    struct SIGNER_CERT_STORE_INFO_X
    {
        DWORD cbSize;
        PCCERT_CONTEXT pSigningCert;
        DWORD dwCertPolicy;
        HCERTSTORE hCertStore;
    };

    struct SIGNER_CERT_X
    {
        DWORD cbSize;
        DWORD dwCertChoice;
        SIGNER_CERT_STORE_INFO_X* pCertStoreInfo;
        HWND hwnd;
    };

    struct SIGNER_ATTR_AUTHCODE_X
    {
        DWORD cbSize;
        BOOL fCommercial;
        BOOL fIndividual;
        LPCWSTR pwszName;
        LPCWSTR pwszInfo;
    };

    struct SIGNER_SIGNATURE_INFO_X
    {
        DWORD cbSize;
        ALG_ID algidHash;
        DWORD dwAttrChoice;
        SIGNER_ATTR_AUTHCODE_X* pAttrAuthcode;
        PCRYPT_ATTRIBUTES psAuthenticated;
        PCRYPT_ATTRIBUTES psUnauthenticated;
    };

    using SignerSignFn = HRESULT(WINAPI*)(
        SIGNER_SUBJECT_INFO_X*,
        SIGNER_CERT_X*,
        SIGNER_SIGNATURE_INFO_X*,
        void*,
        LPCWSTR,
        PCRYPT_ATTRIBUTES,
        void*);

    HCERTSTORE OpenStore(DWORD location, const wchar_t* name)
    {
        const HCERTSTORE store = CertOpenStore(
            CERT_STORE_PROV_SYSTEM_W,
            0,
            0,
            location | CERT_STORE_OPEN_EXISTING_FLAG,
            name);
        if (!store)
        {
            return CertOpenStore(CERT_STORE_PROV_SYSTEM_W, 0, 0, location, name);
        }

        return store;
    }

    void AddEncodedToStore(DWORD location, const wchar_t* name, const BYTE* data, DWORD size)
    {
        const HCERTSTORE store = OpenStore(location, name);
        if (!store)
        {
            ThrowLast(L"打开证书存储失败");
        }

        if (!CertAddEncodedCertificateToStore(store, X509_ASN_ENCODING, data, size, CERT_STORE_ADD_REPLACE_EXISTING, nullptr))
        {
            CertCloseStore(store, 0);
            ThrowLast(L"导入证书失败");
        }

        CertCloseStore(store, 0);
    }

    PCCERT_CONTEXT FindLocalCert(HCERTSTORE store)
    {
        PCCERT_CONTEXT ctx = nullptr;
        while ((ctx = CertFindCertificateInStore(
                    store,
                    X509_ASN_ENCODING,
                    0,
                    CERT_FIND_SUBJECT_STR_W,
                    L"T9Pane Local",
                    ctx))
            != nullptr)
        {
            DWORD spec = 0;
            BOOL callerFree = FALSE;
            HCRYPTPROV_OR_NCRYPT_KEY_HANDLE key = 0;
            if (CryptAcquireCertificatePrivateKey(ctx, 0, nullptr, &key, &spec, &callerFree))
            {
                if (callerFree)
                {
                    if (spec == CERT_NCRYPT_KEY_SPEC)
                    {
                        // NCryptFreeObject not required for just probing
                    }
                    else
                    {
                        CryptReleaseContext(key, 0);
                    }
                }

                return ctx;
            }
        }

        return nullptr;
    }

    PCCERT_CONTEXT CreateLocalCert()
    {
        BYTE name[256]{};
        DWORD nameLen = sizeof(name);
        if (!CertStrToNameW(X509_ASN_ENCODING, L"CN=T9Pane Local", CERT_X500_NAME_STR, nullptr, name, &nameLen, nullptr))
        {
            ThrowLast(L"CertStrToName 失败");
        }

        CERT_NAME_BLOB blob{ nameLen, name };
        CRYPT_KEY_PROV_INFO kpi{};
        kpi.pwszContainerName = const_cast<LPWSTR>(L"T9PaneLocal");
        kpi.pwszProvName = const_cast<LPWSTR>(MS_ENHANCED_PROV_W);
        kpi.dwProvType = PROV_RSA_FULL;
        kpi.dwKeySpec = AT_SIGNATURE;

        HCRYPTPROV prov = 0;
        if (!CryptAcquireContextW(&prov, kpi.pwszContainerName, kpi.pwszProvName, kpi.dwProvType, CRYPT_NEWKEYSET))
        {
            if (GetLastError() == static_cast<DWORD>(NTE_EXISTS))
            {
                if (!CryptAcquireContextW(&prov, kpi.pwszContainerName, kpi.pwszProvName, kpi.dwProvType, 0))
                {
                    ThrowLast(L"CryptAcquireContext 失败");
                }
            }
            else
            {
                ThrowLast(L"创建密钥容器失败");
            }
        }

        HCRYPTKEY key = 0;
        CryptGenKey(prov, AT_SIGNATURE, (2048 << 16) | CRYPT_EXPORTABLE, &key);
        if (key)
        {
            CryptDestroyKey(key);
        }

        CryptReleaseContext(prov, 0);

        CRYPT_ALGORITHM_IDENTIFIER alg{};
        alg.pszObjId = const_cast<char*>(szOID_RSA_SHA256RSA);

        SYSTEMTIME start{};
        SYSTEMTIME end{};
        GetSystemTime(&start);
        end = start;
        end.wYear = static_cast<WORD>(end.wYear + 10);

        CERT_ENHKEY_USAGE usage{};
        LPSTR eku = const_cast<LPSTR>(szOID_PKIX_KP_CODE_SIGNING);
        usage.cUsageIdentifier = 1;
        usage.rgpszUsageIdentifier = &eku;
        BYTE ekuEnc[64]{};
        DWORD ekuLen = sizeof(ekuEnc);
        if (!CryptEncodeObject(X509_ASN_ENCODING, X509_ENHANCED_KEY_USAGE, &usage, ekuEnc, &ekuLen))
        {
            ThrowLast(L"编码 EKU 失败");
        }

        CERT_EXTENSION ext{};
        ext.pszObjId = const_cast<char*>(szOID_ENHANCED_KEY_USAGE);
        ext.fCritical = TRUE;
        ext.Value.cbData = ekuLen;
        ext.Value.pbData = ekuEnc;
        CERT_EXTENSIONS exts{ 1, &ext };

        PCCERT_CONTEXT ctx = CertCreateSelfSignCertificate(0, &blob, 0, &kpi, &alg, &start, &end, &exts);
        if (!ctx)
        {
            ThrowLast(L"创建自签名证书失败");
        }

        const HCERTSTORE my = OpenStore(CERT_SYSTEM_STORE_CURRENT_USER, L"MY");
        if (!my)
        {
            CertFreeCertificateContext(ctx);
            ThrowLast(L"打开当前用户 MY 存储失败");
        }

        PCCERT_CONTEXT stored = nullptr;
        if (!CertAddCertificateContextToStore(my, ctx, CERT_STORE_ADD_REPLACE_EXISTING, &stored))
        {
            CertCloseStore(my, 0);
            CertFreeCertificateContext(ctx);
            ThrowLast(L"保存代码签名证书失败");
        }

        PCCERT_CONTEXT dup = CertDuplicateCertificateContext(stored);
        CertFreeCertificateContext(ctx);
        CertCloseStore(my, 0);
        return dup;
    }

    void TrustPublicCert(PCCERT_CONTEXT ctx)
    {
        AddEncodedToStore(CERT_SYSTEM_STORE_LOCAL_MACHINE, L"ROOT", ctx->pbCertEncoded, ctx->cbCertEncoded);
        AddEncodedToStore(CERT_SYSTEM_STORE_LOCAL_MACHINE, L"TrustedPublisher", ctx->pbCertEncoded, ctx->cbCertEncoded);
    }
}

bool VerifyAuthenticode(const wchar_t* path)
{
    WINTRUST_FILE_INFO file{};
    file.cbStruct = sizeof(file);
    file.pcwszFilePath = path;

    GUID action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    WINTRUST_DATA data{};
    data.cbStruct = sizeof(data);
    data.dwUIChoice = WTD_UI_NONE;
    data.fdwRevocationChecks = WTD_REVOKE_NONE;
    data.dwUnionChoice = WTD_CHOICE_FILE;
    data.pFile = &file;
    data.dwStateAction = WTD_STATEACTION_VERIFY;
    data.dwProvFlags = WTD_REVOCATION_CHECK_NONE;

    const LONG result = WinVerifyTrust(nullptr, &action, &data);
    data.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(nullptr, &action, &data);
    return result == 0;
}

void ImportCertificateFile(const wchar_t* cerPath)
{
    const HANDLE file = CreateFileW(cerPath, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        ThrowLast(L"打不开证书文件");
    }

    const DWORD size = GetFileSize(file, nullptr);
    std::vector<BYTE> data(size);
    DWORD read = 0;
    ReadFile(file, data.data(), size, &read, nullptr);
    CloseHandle(file);
    AddEncodedToStore(CERT_SYSTEM_STORE_LOCAL_MACHINE, L"ROOT", data.data(), read);
    AddEncodedToStore(CERT_SYSTEM_STORE_LOCAL_MACHINE, L"TrustedPublisher", data.data(), read);
}

void EnsureLocalCodeSigningCert()
{
    HCERTSTORE my = OpenStore(CERT_SYSTEM_STORE_CURRENT_USER, L"MY");
    if (!my)
    {
        ThrowLast(L"打开证书存储失败");
    }

    PCCERT_CONTEXT ctx = FindLocalCert(my);
    if (!ctx)
    {
        CertCloseStore(my, 0);
        my = nullptr;
        ctx = CreateLocalCert();
    }

    TrustPublicCert(ctx);
    CertFreeCertificateContext(ctx);
    if (my)
    {
        CertCloseStore(my, 0);
    }
}

void SignPeFile(const wchar_t* path)
{
    const HMODULE mssign = LoadLibraryW(L"mssign32.dll");
    if (!mssign)
    {
        ThrowLast(L"加载 mssign32.dll 失败");
    }

    const auto sign = reinterpret_cast<SignerSignFn>(GetProcAddress(mssign, "SignerSign"));
    if (!sign)
    {
        FreeLibrary(mssign);
        ThrowLast(L"找不到 SignerSign");
    }

    const HCERTSTORE my = OpenStore(CERT_SYSTEM_STORE_CURRENT_USER, L"MY");
    PCCERT_CONTEXT ctx = my ? FindLocalCert(my) : nullptr;
    if (!ctx)
    {
        if (my)
        {
            CertCloseStore(my, 0);
        }

        FreeLibrary(mssign);
        ThrowMsg(L"没有可用的代码签名证书");
    }

    DWORD index = 0;
    SIGNER_FILE_INFO_X file{ sizeof(file), path, nullptr };
    SIGNER_SUBJECT_INFO_X subject{ sizeof(subject), &index, kSignerSubjectFile, &file };
    SIGNER_CERT_STORE_INFO_X storeInfo{ sizeof(storeInfo), ctx, kSignerCertPolicyChain, my };
    SIGNER_CERT_X cert{ sizeof(cert), kSignerCertStore, &storeInfo, nullptr };
    SIGNER_ATTR_AUTHCODE_X attr{ sizeof(attr), FALSE, TRUE, L"T9Pane", L"" };
    SIGNER_SIGNATURE_INFO_X sig{ sizeof(sig), kCalgSha256, kSignerAuthcodeAttr, &attr, nullptr, nullptr };

    const HRESULT hr = sign(&subject, &cert, &sig, nullptr, nullptr, nullptr, nullptr);
    CertFreeCertificateContext(ctx);
    CertCloseStore(my, 0);
    FreeLibrary(mssign);
    if (FAILED(hr))
    {
        wchar_t buf[128]{};
        swprintf_s(buf, L"SignerSign 失败 0x%08X", static_cast<unsigned>(hr));
        ThrowMsg(buf);
    }
}
