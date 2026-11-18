// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <shlwapi.h>
#include <string>

inline std::wstring OfflineNewsUrl(const std::wstring& installation, const std::wstring& locale)
{
    const auto file = installation + L"\\localization\\news\\" + locale + L".html";
    const DWORD attributes = GetFileAttributesW(file.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
        return L"about:blank";

    wchar_t url[2048];
    DWORD length = ARRAYSIZE(url);
    if (FAILED(UrlCreateFromPathW(file.c_str(), url, &length, 0)))
        return L"about:blank";
    return url;
}

inline const wchar_t* OfflineWelcomeTarget(const wchar_t* url, const std::wstring& localUrl)
{
    if (!url || localUrl.empty()) return url;
    if (!_wcsicmp(url, L"http://dnf.qq.com/client/includeImg.htm") ||
        !_wcsicmp(url, L"https://dnf.qq.com/client/includeImg.htm"))
        return localUrl.c_str();
    return url;
}
