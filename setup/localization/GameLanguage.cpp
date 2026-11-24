// Copyright (c) 2026 eirros. All rights reserved.
// Client localization and compatibility hooks for the ACT12 build.
#include <windows.h>
#include <shlwapi.h>
#include <strsafe.h>
#include <intrin.h>
#include <atomic>
#include <string>
#include "LanguageMap.h"
#include "DisplayDecode.h"
#include "OfflineNews.h"
#include "WindowInput.h"
#include "vendor/minhook-1.3.4/include/MinHook.h"

static HMODULE selfModule;
static LanguageMap translations;
static std::wstring installation,locale;
static const wchar_t* face=L"Noto Sans CJK SC";
static wchar_t configuredFace[LF_FACESIZE];
static std::atomic<unsigned long> textHits(0);
static std::atomic<unsigned long> nativeHits(0);
static std::atomic<unsigned long> newsHits(0);
static std::atomic<unsigned long> rawHits(0);
static std::atomic<unsigned long> wideApiHits(0);
static WideDecodeCall originalWideDecode;
static std::once_flag initializationOnce;
static std::atomic<bool> startupGateObserved(false);
static DWORD WINAPI Initialize(void*);
static void EnsureInitialized(){std::call_once(initializationOnce,[](){Initialize(nullptr);});}
static DWORD WINAPI InitializeWorker(void*){EnsureInitialized();return 0;}
using BootVersionCall=BOOL (WINAPI*)(LPOSVERSIONINFOA);
static BootVersionCall originalBootVersion;
static void* startupReturn;
static BOOL WINAPI BootVersion(LPOSVERSIONINFOA info)
{
    // This exact CRT call occurs after the loader releases its lock and before
    // the game's C++ initializers cache UI strings. Initialization is never
    // awaited from DllMain or an arbitrary version-query caller.
    if(_ReturnAddress()==startupReturn){startupGateObserved=true;EnsureInitialized();}
    return originalBootVersion(info);
}
static bool InstallStartupGate(BYTE* image)
{
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(image+reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
    if(nt->OptionalHeader.SizeOfImage!=0x2C1F000||nt->OptionalHeader.AddressOfEntryPoint!=0x93429A)return false;
    BYTE* call=image+0x9342BA;void** slot=reinterpret_cast<void**>(image+0x9F2400);
    if(call[0]!=0xFF||call[1]!=0x15||*reinterpret_cast<DWORD*>(call+2)!=reinterpret_cast<DWORD>(slot))return false;
    originalBootVersion=reinterpret_cast<BootVersionCall>(*slot);if(!originalBootVersion)return false;
    startupReturn=image+0x9342C0;
    DWORD protect;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&protect))return false;
    InterlockedExchangePointer(slot,reinterpret_cast<void*>(&BootVersion));
    DWORD ignored;VirtualProtect(slot,sizeof(void*),protect,&ignored);return true;
}
static const wchar_t* (__cdecl* originalDecode)(void*);
static const wchar_t* (__cdecl* originalRawA)(const char*);
static const wchar_t* (__cdecl* originalRawB)(const char*);
static const wchar_t* (__cdecl* originalUiText)(int);
// Native interface strings can be retained by controls for the entire session.
// Give each numeric string ID its own immutable storage, rather than returning
// the transient ring used by the original byte-to-Unicode conversion routine.
static NativeLanguageCache nativeText;
static int (__stdcall* originalNavigate)(void*,const wchar_t*);
static std::wstring localNews;
typedef HFONT (WINAPI* FontCall)(int,int,int,int,int,DWORD,DWORD,DWORD,DWORD,DWORD,DWORD,DWORD,DWORD,LPCWSTR);
static FontCall originalFont;

static void Log(const char* state)
{
    std::wstring folder=installation+L"\\logs";CreateDirectoryW(folder.c_str(),nullptr);
    wchar_t suffix[80];StringCchPrintfW(suffix,80,L"\\language-client-%lu.json",GetCurrentProcessId());
    std::ofstream out((folder+suffix).c_str(),std::ios::binary|std::ios::trunc);
    std::string id;for(wchar_t c:locale)if(c<128)id.push_back(static_cast<char>(c));
    out<<"{\"locale\":\""<<id<<"\",\"state\":\""<<state<<"\",\"entries\":"<<translations.Count()<<",\"startupGateObserved\":"<<(startupGateObserved.load()?"true":"false")<<",\"translatedConversions\":"<<textHits.load()<<",\"translatedRawStrings\":"<<rawHits.load()<<",\"translatedInterfaceLookups\":"<<nativeHits.load()<<",\"translatedWideApiCalls\":"<<wideApiHits.load()<<",\"localizedNewsNavigations\":"<<newsHits.load()<<",\"clientExecutableModified\":false,\"scriptPvfModified\":false}";
}
static DWORD WINAPI AuditWorker(void*)
{
    const char* state = locale == L"zh-Hans" ? "offline-news" : "enabled";
    for (int i = 0; i < 24; ++i) { Sleep(5000); Log(state); }
    return 0;
}
static void StartAudit(const char* state)
{
    Log(state);
    // Diagnostics run outside the startup gate.
    HANDLE audit = CreateThread(nullptr, 0, AuditWorker, nullptr, 0, nullptr);
    if (audit) CloseHandle(audit);
}
static int WINAPI WideDecode(UINT page,DWORD flags,LPCCH input,int bytes,LPWSTR output,int capacity)
{
    try{bool changed=false;int result=DecodeDisplayText(translations,originalWideDecode,page,flags,input,bytes,output,capacity,changed);if(changed)++wideApiHits;return result;}
    catch(...){return originalWideDecode(page,flags,input,bytes,output,capacity);}
}
static bool InstallWideDecodeImports(BYTE* image)
{
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(image+reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
    auto import=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
    originalWideDecode=reinterpret_cast<WideDecodeCall>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"MultiByteToWideChar"));
    if(!originalWideDecode)return false;unsigned changed=0;
    for(;import->Name;++import)
    {
        if(_stricmp(reinterpret_cast<char*>(image+import->Name),"KERNEL32.dll")||!import->OriginalFirstThunk)continue;
        auto names=reinterpret_cast<IMAGE_THUNK_DATA*>(image+import->OriginalFirstThunk);auto values=reinterpret_cast<IMAGE_THUNK_DATA*>(image+import->FirstThunk);
        for(;names->u1.AddressOfData;++names,++values)
        {
            if(IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal))continue;
            auto item=reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(image+names->u1.AddressOfData);
            if(strcmp(reinterpret_cast<char*>(item->Name),"MultiByteToWideChar"))continue;
            DWORD protect;if(!VirtualProtect(&values->u1.Function,sizeof(void*),PAGE_READWRITE,&protect))return false;
            InterlockedExchangePointer(reinterpret_cast<void**>(&values->u1.Function),reinterpret_cast<void*>(&WideDecode));
            DWORD ignored;VirtualProtect(&values->u1.Function,sizeof(void*),protect,&ignored);++changed;
        }
    }
    return changed==2;
}
static const wchar_t* __cdecl UiText(int id)
{
    // Call outside our mutex: first access may initialize the game's string
    // table and invoke Decode recursively while parsing its resource.
    const wchar_t* source=originalUiText(id);
    try
    {
        bool changed=false;const wchar_t* result=nativeText.Lookup(id,source,translations,changed);
        if(changed)++nativeHits;
        return result;
    }
    catch(...){return source;}
}
static int __stdcall Navigate(void* browser,const wchar_t* url)
{
    const wchar_t* target = OfflineWelcomeTarget(url, localNews);
    if (target != url) ++newsHits;
    return originalNavigate(browser, target);
}
static const wchar_t* __cdecl Decode(void* input)
{
    const wchar_t* source=originalDecode(input);
    try
    {
        const wchar_t* result=translations.Lookup(source);
        if(result!=source)++textHits;
        return result;
    }
    catch(...){return source;}
}
static const wchar_t* RawLookup(const wchar_t* source)
{
    try{const wchar_t* result=translations.Lookup(source);if(result!=source)++rawHits;return result;}
    catch(...){return source;}
}
static const wchar_t* __cdecl RawA(const char* input){return RawLookup(originalRawA(input));}
static const wchar_t* __cdecl RawB(const char* input){return RawLookup(originalRawB(input));}
static HFONT WINAPI Font(int height,int width,int escapement,int orientation,int weight,DWORD italic,DWORD underline,DWORD strike,DWORD charset,DWORD out,DWORD clip,DWORD quality,DWORD pitch,LPCWSTR name)
{
    if(name&&(!_wcsicmp(name,L"SimSun")||!_wcsicmp(name,L"XPSimSun")||!_wcsicmp(name,L"Gasinamu")||!_wcsicmp(name,L"\x5b8b\x4f53")))
    {name=face;charset=DEFAULT_CHARSET;}
    return originalFont(height,width,escapement,orientation,weight,italic,underline,strike,charset,out,clip,quality,pitch,name);
}
static bool InstallFontImport(BYTE* image)
{
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(image+reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
    auto rva=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    auto import=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image+rva);
    for(;import->Name;++import)
    {
        if(_stricmp(reinterpret_cast<char*>(image+import->Name),"GDI32.dll")||!import->OriginalFirstThunk)continue;
        auto names=reinterpret_cast<IMAGE_THUNK_DATA*>(image+import->OriginalFirstThunk);
        auto values=reinterpret_cast<IMAGE_THUNK_DATA*>(image+import->FirstThunk);
        for(;names->u1.AddressOfData;++names,++values)
        {
            if(IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal))continue;
            auto item=reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(image+names->u1.AddressOfData);
            if(strcmp(reinterpret_cast<char*>(item->Name),"CreateFontW"))continue;
            DWORD protect;if(!VirtualProtect(&values->u1.Function,sizeof(void*),PAGE_READWRITE,&protect))return false;
            originalFont=reinterpret_cast<FontCall>(InterlockedExchangePointer(reinterpret_cast<void**>(&values->u1.Function),reinterpret_cast<void*>(&Font)));
            DWORD ignored;VirtualProtect(&values->u1.Function,sizeof(void*),protect,&ignored);return true;
        }
    }
    return false;
}
using TutorialModuleCheck = unsigned char (__thiscall*)(void*, int);
static TutorialModuleCheck originalTutorialModuleCheck;

static unsigned char __fastcall CanStartSeriaIntroduction(void* tutorial, void*, int module)
{
    constexpr int tutorialDungeonStarted = 30;
    constexpr int tutorialDungeonFinished = 31;
    // Returning via the dungeon menu persists module 31 on the server. The
    // client's town transition can run before that updated flag is in memory.
    return originalTutorialModuleCheck(tutorial, module) ||
        (module == tutorialDungeonFinished && originalTutorialModuleCheck(tutorial, tutorialDungeonStarted));
}

static bool InstallSeriaIntroductionHook(BYTE* image)
{
    BYTE* gate = image + 0x70F23F;
    BYTE* moduleCheck = image + 0x70B800;
    const BYTE gateSignature[] = {0x6A,0x1F,0xE8,0xBA,0xC5,0xFF,0xFF,0x3C,0x01,0x0F,0x85,0xAA,0,0,0};
    const BYTE checkSignature[] = {0x55,0x8B,0xEC,0x53,0x56,0x8B,0xB1,0x9C,0,0,0};
    if (memcmp(gate, gateSignature, sizeof(gateSignature)) ||
        memcmp(moduleCheck, checkSignature, sizeof(checkSignature))) return false;

    // Replace only the call in the first dungeon-to-town introduction gate.
    // Its original scene, quest completion and quest capacity checks still run.
    BYTE* displacement = gate + 3;
    DWORD protection;
    if (!VirtualProtect(displacement, sizeof(DWORD), PAGE_EXECUTE_READWRITE, &protection)) return false;
    originalTutorialModuleCheck = reinterpret_cast<TutorialModuleCheck>(moduleCheck);
    const DWORD target = reinterpret_cast<DWORD>(&CanStartSeriaIntroduction);
    const DWORD offset = target - reinterpret_cast<DWORD>(displacement + sizeof(DWORD));
    memcpy(displacement, &offset, sizeof(offset));
    DWORD unused;
    VirtualProtect(displacement, sizeof(DWORD), protection, &unused);
    FlushInstructionCache(GetCurrentProcess(), gate + 2, 1 + sizeof(DWORD));
    return true;
}

static void LocalizeSkillPlanButtons()
{
    const wchar_t* first;
    const wchar_t* second;
    if (locale == L"en-US") { first = L"Plan A"; second = L"Plan B"; }
    else if (locale == L"ja-JP") { first = L"プラン A"; second = L"プラン B"; }
    else if (locale == L"ko-KR") { first = L"설정 A"; second = L"설정 B"; }
    else if (locale == L"zh-Hant") { first = L"方案 A"; second = L"方案 B"; }
    else return;

    const std::wstring path = installation + L"\\DNF60_ACT12\\ACT12\\version.dll";
    auto module = reinterpret_cast<BYTE*>(GetModuleHandleW(path.c_str()));
    if (!module) return;
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(module + dos->e_lfanew);
    if (nt->OptionalHeader.SizeOfImage != 0x22000) return;

    // The legacy extension selects these two literal pointers with a mov/mov/cmove.
    // Leave the control setup and its text copy routine in place.
    const BYTE firstOffset = 1, secondOffset = 6;
    BYTE* instructions = module + 0x1945;
    BYTE labels[] = {0xB8, 0, 0, 0, 0, 0xBA, 0, 0, 0, 0, 0x0F, 0x44, 0xD0};
    auto originalFirst = reinterpret_cast<const wchar_t*>(module + 0x17178);
    auto originalSecond = reinterpret_cast<const wchar_t*>(module + 0x17170);
    memcpy(labels + firstOffset, &originalFirst, sizeof(originalFirst));
    memcpy(labels + secondOffset, &originalSecond, sizeof(originalSecond));
    if (memcmp(instructions, labels, sizeof(labels)) ||
        memcmp(originalFirst, L"方案一", sizeof(L"方案一")) ||
        memcmp(originalSecond, L"方案二", sizeof(L"方案二"))) return;

    memcpy(labels + firstOffset, &first, sizeof(first));
    memcpy(labels + secondOffset, &second, sizeof(second));
    DWORD protection;
    if (!VirtualProtect(instructions, sizeof(labels), PAGE_EXECUTE_READWRITE, &protection)) return;
    memcpy(instructions, labels, sizeof(labels));
    DWORD unused;
    VirtualProtect(instructions, sizeof(labels), protection, &unused);
    FlushInstructionCache(GetCurrentProcess(), instructions, sizeof(labels));
}

static DWORD WINAPI Initialize(void*)
{
    wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);
    if(_wcsicmp(PathFindFileNameW(path),L"DNF.exe"))return 0;
    GetModuleFileNameW(selfModule,path,32768);PathRemoveFileSpecW(path);
    PathRemoveFileSpecW(path);PathRemoveFileSpecW(path);installation=path;
    wchar_t id[32];std::wstring settings=installation+L"\\localization\\selected.ini";
    GetPrivateProfileStringW(L"Localization",L"Language",L"zh-Hans",id,32,settings.c_str());locale=id;
    // The manifest controls installed packs. Additional BCP-47 tags can use the
    // same Unicode pipeline after their text/font coverage has been validated.
    for(wchar_t c:locale)if(!((c>=L'a'&&c<=L'z')||(c>=L'A'&&c<=L'Z')||(c>=L'0'&&c<=L'9')||c==L'-')){Log("invalid-language-id");return 0;}
    BYTE* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(image + reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
    BYTE* newsTarget = image + 0x7E7110;
    const BYTE newsSignature[] = {0x55,0x8B,0xEC,0x8B,0x45,0x0C,0x8B,0x4D,0x08,0x50,0x51};
    if (nt->OptionalHeader.SizeOfImage != 0x2C1F000 || memcmp(newsTarget, newsSignature, sizeof(newsSignature)))
    { Log("unsupported-client"); return 0; }
    AradWindowInput::Install(image);
    if (!InstallSeriaIntroductionHook(image)) Log("seria-introduction-hook-unavailable");
    localNews = OfflineNewsUrl(installation, locale);
    // Use the local welcome page with the original Chinese text and font path.
    if (locale == L"zh-Hans")
    {
        if (MH_Initialize() != MH_OK ||
            MH_CreateHook(newsTarget, reinterpret_cast<void*>(&Navigate), reinterpret_cast<void**>(&originalNavigate)) != MH_OK ||
            MH_EnableHook(newsTarget) != MH_OK)
        { Log("news-hook-installation-failed"); return 0; }
        StartAudit("offline-news");
        return 0;
    }
    if(locale==L"zh-Hant")face=L"Noto Sans CJK TC";
    else if(locale==L"ja-JP")face=L"Noto Sans CJK JP";
    else if(locale==L"ko-KR")face=L"Noto Sans CJK KR";
    GetPrivateProfileStringW(L"Localization",L"Font",face,configuredFace,LF_FACESIZE,settings.c_str());
    face=configuredFace;
    if(!translations.Load(installation+L"\\localization\\packs\\"+locale+L".arlp")){Log("invalid-pack");return 0;}
    const std::wstring names=installation+L"\\localization\\protected-names.arnm";
    if(PathFileExistsW(names.c_str())&&!translations.LoadProtectedNames(names)){Log("invalid-name-protection");return 0;}
    BYTE* target=image+0x82B360;
    BYTE* rawA=image+0x82B1B0;
    BYTE* rawB=image+0x82B2D0;
    BYTE* uiTarget=image+0x7E62B0;
    const BYTE signature[]={0x55,0x8B,0xEC,0x8B,0x4D,0x08,0x64,0x8B,0x15,0x2C,0,0,0,0x53,0x56,0x8B,0x71,0x14,0x57};
    const BYTE uiSignature[]={0x55,0x8B,0xEC,0x6A,0xFF,0x68};
    const BYTE rawSignature[]={0x55,0x8B,0xEC,0x64,0x8B,0x0D,0x2C,0,0,0};
    if(memcmp(target,signature,sizeof(signature))||
       memcmp(uiTarget,uiSignature,sizeof(uiSignature))||
       *reinterpret_cast<DWORD*>(uiTarget+6)!=reinterpret_cast<DWORD>(image+0x9D75CD)||
       memcmp(rawA,rawSignature,sizeof(rawSignature))||memcmp(rawB,rawSignature,sizeof(rawSignature)))
    {Log("unsupported-client");return 0;}
    std::wstring font=installation+L"\\localization\\fonts\\NotoSansCJK-Regular.ttc";
    if(!AddFontResourceExW(font.c_str(),FR_PRIVATE,nullptr)){Log("font-unavailable");return 0;}
    if(MH_Initialize()!=MH_OK||MH_CreateHook(target,reinterpret_cast<void*>(&Decode),reinterpret_cast<void**>(&originalDecode))!=MH_OK||
       MH_CreateHook(uiTarget,reinterpret_cast<void*>(&UiText),reinterpret_cast<void**>(&originalUiText))!=MH_OK||
       MH_CreateHook(rawA,reinterpret_cast<void*>(&RawA),reinterpret_cast<void**>(&originalRawA))!=MH_OK||
       MH_CreateHook(rawB,reinterpret_cast<void*>(&RawB),reinterpret_cast<void**>(&originalRawB))!=MH_OK||
       MH_CreateHook(newsTarget,reinterpret_cast<void*>(&Navigate),reinterpret_cast<void**>(&originalNavigate))!=MH_OK)
    {Log("hook-creation-failed");return 0;}
    if(!InstallFontImport(image)||!InstallWideDecodeImports(image)||MH_QueueEnableHook(target)!=MH_OK||MH_QueueEnableHook(uiTarget)!=MH_OK||
       MH_QueueEnableHook(rawA)!=MH_OK||MH_QueueEnableHook(rawB)!=MH_OK||
       MH_QueueEnableHook(newsTarget)!=MH_OK||MH_ApplyQueued()!=MH_OK)
    {Log("hook-installation-failed");return 0;}
    LocalizeSkillPlanButtons();
    StartAudit("enabled");
    return 0;
}

// The original scoring extension remains a dependency and keeps its own startup.
// VERSION.dll uses this documented extension export, so DNF.exe stays untouched.
#pragma comment(linker,"/export:A12LiveScoreInitialize=a12_score_base.A12LiveScoreInitialize,@1")
BOOL WINAPI DllMain(HMODULE module,DWORD reason,LPVOID)
{
    if(reason==DLL_PROCESS_ATTACH)
    {
        selfModule=module;DisableThreadLibraryCalls(module);
        InstallStartupGate(reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr)));
        HANDLE worker=CreateThread(nullptr,0,InitializeWorker,nullptr,0,nullptr);if(worker)CloseHandle(worker);
    }
    return TRUE;
}
