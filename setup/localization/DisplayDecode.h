// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include "LanguageMap.h"
using WideDecodeCall=int (WINAPI*)(UINT,DWORD,LPCCH,int,LPWSTR,int);
// Match the Windows API's query/write contract. Opaque buffers, resource paths,
// unsupported code pages, and insufficient fixed buffers retain the original
// conversion. No protocol bytes or persistent game data are rewritten.
inline int DecodeDisplayText(const LanguageMap& dictionary,WideDecodeCall original,UINT page,DWORD flags,LPCCH input,int bytes,LPWSTR output,int capacity,bool& changed)
{
    changed=false;
    if(!input||(page!=936&&page!=CP_ACP&&page!=CP_UTF8)||bytes==0||bytes < -1||bytes>32768||capacity<0)return original(page,flags,input,bytes,output,capacity);
    int count=original(page,flags,input,bytes,nullptr,0);
    if(count<=0||count>8192)return original(page,flags,input,bytes,output,capacity);
    std::wstring decoded(count,L'\0');
    if(original(page,flags,input,bytes,&decoded[0],count)!=count)return original(page,flags,input,bytes,output,capacity);
    if(bytes==-1&&decoded.back()==L'\0')decoded.pop_back();
    if(decoded.find(L'\0')!=std::wstring::npos)return original(page,flags,input,bytes,output,capacity);
    const wchar_t* target=dictionary.Lookup(decoded.c_str());
    if(target==decoded.c_str())return original(page,flags,input,bytes,output,capacity);
    size_t size=wcslen(target)+(bytes==-1?1:0);
    if(size>INT_MAX||(!output&&capacity!=0)||(capacity&&size>static_cast<size_t>(capacity)))return original(page,flags,input,bytes,output,capacity);
    changed=true;
    if(capacity)memcpy(output,target,size*sizeof(wchar_t));
    return static_cast<int>(size);
}
