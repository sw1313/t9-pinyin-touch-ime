#include "T9Ime.h"
#include "Guids.h"
#include <new>

static LONG g_locks = 0;
HINSTANCE g_hInst = nullptr;

void DllAddLock() { InterlockedIncrement(&g_locks); }
void DllReleaseLock() { InterlockedDecrement(&g_locks); }

class ClassFactory : public IClassFactory
{
public:
    ClassFactory() : _ref(1) { DllAddLock(); }
    ~ClassFactory() { DllReleaseLock(); }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory*>(this);
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

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv)
    {
        if (outer)
        {
            return CLASS_E_NOAGGREGATION;
        }

        auto* ime = new (std::nothrow) T9Ime();
        if (!ime)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = ime->QueryInterface(riid, ppv);
        ime->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL lock)
    {
        if (lock)
        {
            DllAddLock();
        }
        else
        {
            DllReleaseLock();
        }
        return S_OK;
    }

private:
    LONG _ref;
};

BOOL APIENTRY DllMain(HINSTANCE hinst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hInst = hinst;
        DisableThreadLibraryCalls(hinst);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    // T9Ime owns pipe workers and window procedures that can still be completing
    // after TSF releases its last COM reference. Keep the module resident until
    // the host process exits so no callback can jump into an unloaded DLL.
    return S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (rclsid != CLSID_T9Ime)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();
    if (!factory)
    {
        return E_OUTOFMEMORY;
    }

    const auto hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllRegisterServer()
{
    return RegisterT9Ime();
}

STDAPI DllUnregisterServer()
{
    return UnregisterT9Ime();
}
