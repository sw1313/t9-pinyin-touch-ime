#include "TsfFunctions.h"
#include <oleauto.h>
#include <new>
#include <string>
#include <vector>

namespace
{
    std::vector<std::wstring> g_searchWords;
    std::wstring g_composeText;

    class TipCandidateString : public ITfCandidateString
    {
    public:
        TipCandidateString(ULONG index, const wchar_t* text)
            : _ref(1), _index(index), _text(SysAllocString(text ? text : L""))
        {
        }

        ~TipCandidateString()
        {
            if (_text)
            {
                SysFreeString(_text);
            }
        }

        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            if (!ppv)
            {
                return E_INVALIDARG;
            }

            if (riid == IID_IUnknown || riid == IID_ITfCandidateString)
            {
                *ppv = static_cast<ITfCandidateString*>(this);
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

        STDMETHODIMP GetString(BSTR* pbstr)
        {
            if (!pbstr)
            {
                return E_INVALIDARG;
            }

            *pbstr = SysAllocString(_text ? _text : L"");
            return *pbstr ? S_OK : E_OUTOFMEMORY;
        }

        STDMETHODIMP GetIndex(ULONG* pnIndex)
        {
            if (!pnIndex)
            {
                return E_INVALIDARG;
            }

            *pnIndex = _index;
            return S_OK;
        }

    private:
        LONG _ref;
        ULONG _index;
        BSTR _text;
    };

    class TipCandidateList : public ITfCandidateList
    {
    public:
        TipCandidateList() : _ref(1) {}

        ~TipCandidateList()
        {
            for (auto* item : _items)
            {
                item->Release();
            }
        }

        HRESULT Add(const wchar_t* text)
        {
            auto* item = new (std::nothrow) TipCandidateString(
                static_cast<ULONG>(_items.size()),
                text);
            if (!item)
            {
                return E_OUTOFMEMORY;
            }

            _items.push_back(item);
            return S_OK;
        }

        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            if (!ppv)
            {
                return E_INVALIDARG;
            }

            if (riid == IID_IUnknown || riid == IID_ITfCandidateList)
            {
                *ppv = static_cast<ITfCandidateList*>(this);
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

        STDMETHODIMP EnumCandidates(IEnumTfCandidates**)
        {
            return E_NOTIMPL;
        }

        STDMETHODIMP GetCandidate(ULONG nIndex, ITfCandidateString** ppCand)
        {
            if (!ppCand)
            {
                return E_INVALIDARG;
            }

            if (nIndex >= _items.size())
            {
                *ppCand = nullptr;
                return E_INVALIDARG;
            }

            *ppCand = _items[nIndex];
            (*ppCand)->AddRef();
            return S_OK;
        }

        STDMETHODIMP GetCandidateNum(ULONG* pnCnt)
        {
            if (!pnCnt)
            {
                return E_INVALIDARG;
            }

            *pnCnt = static_cast<ULONG>(_items.size());
            return S_OK;
        }

        STDMETHODIMP SetResult(ULONG, TfCandidateResult)
        {
            return S_OK;
        }

    private:
        LONG _ref;
        std::vector<ITfCandidateString*> _items;
    };

    class SearchCandidateProvider : public ITfFnSearchCandidateProvider
    {
    public:
        SearchCandidateProvider() : _ref(1) {}

        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            if (!ppv)
            {
                return E_INVALIDARG;
            }

            if (riid == IID_IUnknown
                || riid == IID_ITfFunction
                || riid == IID_ITfFnSearchCandidateProvider)
            {
                *ppv = static_cast<ITfFnSearchCandidateProvider*>(this);
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

        STDMETHODIMP GetDisplayName(BSTR* pbstrName)
        {
            if (!pbstrName)
            {
                return E_INVALIDARG;
            }

            *pbstrName = SysAllocString(L"SearchCandidateProvider");
            return *pbstrName ? S_OK : E_OUTOFMEMORY;
        }

        STDMETHODIMP GetSearchCandidates(
            BSTR,
            BSTR,
            ITfCandidateList** pplist)
        {
            if (!pplist)
            {
                return E_INVALIDARG;
            }

            auto* list = new (std::nothrow) TipCandidateList();
            if (!list)
            {
                *pplist = nullptr;
                return E_OUTOFMEMORY;
            }

            for (const auto& word : g_searchWords)
            {
                if (FAILED(list->Add(word.c_str())))
                {
                    list->Release();
                    *pplist = nullptr;
                    return E_OUTOFMEMORY;
                }
            }

            *pplist = list;
            return S_OK;
        }

        STDMETHODIMP SetResult(BSTR, BSTR, BSTR)
        {
            return E_NOTIMPL;
        }

    private:
        LONG _ref;
    };
}

void SetSearchCandidateCache(const wchar_t* packedCompose)
{
    g_searchWords.clear();
    g_composeText.clear();
    if (!packedCompose)
    {
        return;
    }

    const wchar_t* start = packedCompose;
    auto take = [&](bool first)
    {
        const wchar_t* end = start;
        while (*end && *end != 0x001E)
        {
            ++end;
        }

        if (first)
        {
            g_composeText.assign(start, end);
        }
        else if (end > start && g_searchWords.size() < 12)
        {
            g_searchWords.emplace_back(start, end);
        }

        start = *end ? end + 1 : end;
    };

    take(true);
    while (*start)
    {
        take(false);
    }
}

void ClearSearchCandidateCache()
{
    g_searchWords.clear();
    g_composeText.clear();
}

const wchar_t* ComposeTextFromPayload(const wchar_t* packedCompose)
{
    SetSearchCandidateCache(packedCompose);
    return g_composeText.c_str();
}

HRESULT CreateSearchCandidateProvider(ITfFnSearchCandidateProvider** out)
{
    if (!out)
    {
        return E_INVALIDARG;
    }

    auto* provider = new (std::nothrow) SearchCandidateProvider();
    if (!provider)
    {
        *out = nullptr;
        return E_OUTOFMEMORY;
    }

    *out = provider;
    return S_OK;
}
