#pragma once
#include <msctf.h>
#include <ctffunc.h>

HRESULT CreateSearchCandidateProvider(ITfFnSearchCandidateProvider** out);
void SetSearchCandidateCache(const wchar_t* packedCompose);
void ClearSearchCandidateCache();
const wchar_t* ComposeTextFromPayload(const wchar_t* packedCompose);
