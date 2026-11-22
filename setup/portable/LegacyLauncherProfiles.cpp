// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlwapi.h>
#include <strsafe.h>
#pragma comment(linker, "/export:AccountEditSubclass=a12_account_fix.AccountEditSubclass,@1")
#pragma comment(linker, "/export:BeginLogin=a12_account_fix.BeginLogin,@2")
#pragma comment(linker, "/export:ReadLauncherText=a12_account_fix.ReadLauncherText,@3")
#pragma comment(linker, "/export:SubmitRegistration=a12_account_fix.SubmitRegistration,@4")
#pragma comment(linker, "/export:TlsRequest=a12_account_fix.TlsRequest,@5")

// A small forwarder loaded by the original launcher's existing extension point.
// All five original login exports retain their original implementation and ABI.
static BOOL CALLBACK FindLauncher(HWND window, LPARAM context)
{
    DWORD owner=0;
    GetWindowThreadProcessId(window,&owner);
    RECT rect={};
    if(owner==GetCurrentProcessId() && IsWindowVisible(window) &&
       GetWindow(window,GW_OWNER)==NULL && GetClientRect(window,&rect) &&
       rect.right>=500 && rect.bottom>=400)
    {
        *reinterpret_cast<HWND*>(context)=window;
        return FALSE;
    }
    return TRUE;
}

static DWORD WINAPI AttachProfiles(void*)
{
    wchar_t exe[MAX_PATH]={};
    GetModuleFileNameW(NULL,exe,MAX_PATH);
    if(lstrcmpiW(PathFindFileNameW(exe),L"LegacyLauncher.exe")!=0)return 0;
    HWND host=NULL;
    for(unsigned i=0;i<150 && !host;++i){EnumWindows(FindLauncher,reinterpret_cast<LPARAM>(&host));if(!host)Sleep(100);}
    if(!host)return 0;
    wchar_t root[MAX_PATH]={};
    StringCchCopyW(root,MAX_PATH,exe);
    PathRemoveFileSpecW(root);PathRemoveFileSpecW(root);PathRemoveFileSpecW(root);
    wchar_t controller[MAX_PATH]={},command[2*MAX_PATH]={};
    StringCchPrintfW(controller,MAX_PATH,L"%s\\AradHomeward.exe",root);
    StringCchPrintfW(command,2*MAX_PATH,L"\"%s\" --launcher-ui %lu %llu",controller,
        GetCurrentProcessId(),static_cast<unsigned long long>(reinterpret_cast<UINT_PTR>(host)));
    STARTUPINFOW start={sizeof(start)};start.dwFlags=STARTF_USESHOWWINDOW;start.wShowWindow=SW_HIDE;
    PROCESS_INFORMATION process={};
    if(CreateProcessW(controller,command,NULL,NULL,FALSE,CREATE_NO_WINDOW,NULL,root,&start,&process))
    {CloseHandle(process.hThread);CloseHandle(process.hProcess);}
    else MessageBoxW(host,L"账号选择区未能启动，请完整解压游戏包后重试。",L"阿拉德·归途",MB_OK|MB_ICONWARNING);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID)
{
    if(reason==DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        HANDLE worker=CreateThread(NULL,0,AttachProfiles,NULL,0,NULL);
        if(worker)CloseHandle(worker);
    }
    return TRUE;
}
