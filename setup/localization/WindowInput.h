// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <windowsx.h>
#include "GameSettingsBridge.h"
#include "PixelPresentation.h"

namespace AradWindowInput
{
    constexpr LONG gameWidth = 640;
    constexpr LONG gameHeight = 480;
    constexpr wchar_t readyProperty[] = L"AradHomeward.WindowInputReady";
    constexpr wchar_t widthProperty[] = L"AradHomeward.WindowScaleWidth";
    constexpr wchar_t sizeProperty[] = L"AradHomeward.WindowScaleSize";
    constexpr wchar_t chatNativeWidthProperty[] = L"AradHomeward.ChatNativeWidth";
    constexpr wchar_t chatScaleSizeProperty[] = L"AradHomeward.ChatScaleSize";
    static HWND gameWindow;
    static WNDPROC gameProcedure;
    static HWND chatWindow;
    static WNDPROC chatProcedure;
    static LONG chatNativeWidth;
    static bool installed;
    static bool pixelPresentationAvailable;
    static decltype(&DispatchMessageW) dispatchMessage;
    static decltype(&TranslateMessage) translateMessage;
    static const MSG* handledBindingMessage;
    static decltype(&ScreenToClient) screenToClient;
    static decltype(&ClientToScreen) clientToScreen;
    using DevicePresent = HRESULT (WINAPI*)(void*, const RECT*, const RECT*, HWND, const RGNDATA*);

    static void EnableDpiAwareness()
    {
        // Set awareness before either viewport exists so Windows presents
        // the resized back buffers at their physical pixel dimensions.
        HMODULE user = GetModuleHandleW(L"user32.dll");
        const auto setContext = reinterpret_cast<decltype(&SetProcessDpiAwarenessContext)>(
            GetProcAddress(user, "SetProcessDpiAwarenessContext"));
        if (!setContext || !setContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            SetProcessDPIAware(); // Windows 7 and earlier Windows 10 builds.
    }

    static LONG CanvasWidth(HWND window)
    {
        if (!window) return 0;
        if (window == gameWindow) return gameWidth;
        return window == chatWindow ? chatNativeWidth : 0;
    }

    static bool LegacyFullscreen()
    {
        return gameWindow && GetPropW(gameWindow, AradGameSettings::screenModeProperty) ==
            ULongToHandle(AradGameSettings::fullScreen + 1);
    }

    static bool ClientArea(HWND window, RECT& client)
    {
        return GetClientRect(window, &client) && client.right > 0 && client.bottom > 0;
    }

    static bool ScaledClient(HWND window, RECT& client, LONG& nativeWidth)
    {
        nativeWidth = CanvasWidth(window);
        return nativeWidth && ClientArea(window, client) &&
            (client.right != nativeWidth || client.bottom != gameHeight) && !LegacyFullscreen();
    }

    static LONG ScalePixel(LONG coordinate, LONG source, LONG target)
    {
        // Sample the same pixel centers as the presentation pass.
        const LONGLONG numerator = (2LL * coordinate + 1) * target;
        const LONGLONG divisor = 2LL * source;
        return static_cast<LONG>((numerator < 0 ? numerator - divisor + 1 : numerator) / divisor);
    }

    static void ToNativePoint(POINT& point, const RECT& client, LONG nativeWidth)
    {
        point.x = ScalePixel(point.x, client.right, nativeWidth);
        point.y = ScalePixel(point.y, client.bottom, gameHeight);
    }

    static HRESULT WINAPI PresentFullFrame(void* device, const RECT* source, const RECT* destination,
        HWND target, const RGNDATA* dirty)
    {
        constexpr size_t presentSlot = 15; // IDirect3DDevice8::Present
        constexpr HRESULT invalidCall = static_cast<HRESULT>(0x8876086c);
        const auto present = (*static_cast<DevicePresent**>(device))[presentSlot];
        const HWND window = target ? target : gameWindow;
        const LONG nativeWidth = CanvasWidth(window);
        RECT client;
        if (nativeWidth && !LegacyFullscreen() && ClientArea(window, client))
        {
            // The back buffer contains a fixed canvas for each pane. Scale it
            // to the current client area even while a resize is in flight.
            const RECT canvas = { 0, 0, nativeWidth, gameHeight };
            if (pixelPresentationAvailable && !dirty && !EqualRect(&canvas, &client) &&
                AradPixelPresentation::PresentScaled(device, window, canvas, client, window == chatWindow))
                return S_OK;
            const HRESULT result = present(device, &canvas, &client, target, dirty);
            if (result != invalidCall) return result;
        }
        return present(device, source, destination, target, dirty);
    }

    template <size_t length>
    static void RedirectDeviceCall(BYTE* site, const BYTE (&original)[length], const void* callback)
    {
        // Drop the two-byte vtable load to make room for the longer direct
        // call. The original argument pushes and stack cleanup stay intact.
        constexpr size_t callOffset = length - 5;
        memcpy(site, original + 2, callOffset);
        site[callOffset] = 0xe8;
        const DWORD relative = static_cast<DWORD>(reinterpret_cast<UINT_PTR>(callback) -
            reinterpret_cast<UINT_PTR>(site + length));
        memcpy(site + callOffset + 1, &relative, sizeof(relative));
    }

    static bool InstallDeviceCleanup(BYTE* image)
    {
        BYTE* release = image + 0x876756;
        BYTE* reset = image + 0x876ff8;
        const BYTE releaseCode[] = {0x8b,0x08, 0x50, 0xff,0x51,0x08};
        const BYTE resetCode[] = {0x8b,0x08, 0x81,0xc6,0x44,0x20,0x00,0x00, 0x56,0x50, 0xff,0x51,0x38};
        if (memcmp(release, releaseCode, sizeof(releaseCode)) || memcmp(reset, resetCode, sizeof(resetCode))) return false;
        const size_t bytes = reset + sizeof(resetCode) - release;
        DWORD protection, ignored;
        if (!VirtualProtect(release, bytes, PAGE_EXECUTE_READWRITE, &protection)) return false;
        RedirectDeviceCall(release, releaseCode, reinterpret_cast<void*>(&AradPixelPresentation::ReleaseDevice));
        RedirectDeviceCall(reset, resetCode, reinterpret_cast<void*>(&AradPixelPresentation::ResetDevice));
        VirtualProtect(release, bytes, protection, &ignored);
        FlushInstructionCache(GetCurrentProcess(), release, bytes);
        return true;
    }

    static bool InstallFramePresentation(BYTE* image)
    {
        // The renderer has two full-frame calls, for an explicit HWND and for
        // the default game window. Partial presents use a separate call site.
        BYTE* target = image + 0x877540;
        BYTE* main = image + 0x87755f;
        const BYTE targetCode[] = {0x8b,0x08, 0x6a,0x00, 0x57, 0x8d,0x55,0xf0, 0x52,0x52,0x50, 0xff,0x51,0x3c};
        const BYTE mainCode[] = {0x8b,0x08, 0x83,0xc4,0x04, 0x6a,0x00,0x6a,0x00,0x6a,0x00,0x6a,0x00,0x50, 0xff,0x51,0x3c};
        if (memcmp(target, targetCode, sizeof(targetCode)) || memcmp(main, mainCode, sizeof(mainCode))) return false;
        const size_t bytes = main + sizeof(mainCode) - target;
        DWORD protection, ignored;
        if (!VirtualProtect(target, bytes, PAGE_EXECUTE_READWRITE, &protection)) return false;
        RedirectDeviceCall(target, targetCode, reinterpret_cast<void*>(&PresentFullFrame));
        RedirectDeviceCall(main, mainCode, reinterpret_cast<void*>(&PresentFullFrame));
        VirtualProtect(target, bytes, protection, &ignored);
        FlushInstructionCache(GetCurrentProcess(), target, bytes);
        pixelPresentationAvailable = InstallDeviceCleanup(image);
        return true;
    }

    static BOOL WINAPI GameScreenToClient(HWND window, LPPOINT point)
    {
        BOOL ok = screenToClient(window, point);
        RECT client;
        LONG nativeWidth;
        if (ok && ScaledClient(window, client, nativeWidth)) ToNativePoint(*point, client, nativeWidth);
        return ok;
    }

    static BOOL WINAPI GameClientToScreen(HWND window, LPPOINT point)
    {
        RECT client;
        LONG nativeWidth;
        if (point && ScaledClient(window, client, nativeWidth))
        {
            point->x = ScalePixel(point->x, nativeWidth, client.right);
            point->y = ScalePixel(point->y, gameHeight, client.bottom);
        }
        return clientToScreen(window, point);
    }

    static LRESULT CALLBACK ViewportProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        const bool isChat = window == chatWindow;
        WNDPROC previous = isChat ? chatProcedure : gameProcedure;
        if (!isChat && (message == WM_KILLFOCUS || (message == WM_ACTIVATEAPP && !wParam)))
            AradGameSettings::SuspendInput();
        if (!isChat && message == WM_SIZE) AradGameSettings::PublishScreenMode(AradGameSettings::Config());
        if (isChat && message == WM_SIZE && wParam != SIZE_MINIMIZED)
        {
            RECT client;
            LONG nativeWidth;
            // Preserve the original chat layout while scaling its presentation.
            if (ScaledClient(window, client, nativeWidth)) return 0;
        }
        if (message >= WM_MOUSEFIRST && message <= WM_MOUSELAST &&
            message != WM_MOUSEWHEEL && message != WM_MOUSEHWHEEL)
        {
            RECT client;
            LONG nativeWidth;
            if (ScaledClient(window, client, nativeWidth))
            {
                POINT point = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                ToNativePoint(point, client, nativeWidth);
                lParam = MAKELPARAM(point.x, point.y);
            }
        }
        if (message == WM_NCDESTROY)
        {
            AradPixelPresentation::CloseWindow(isChat);
            if (isChat)
            {
                RemovePropW(window, chatNativeWidthProperty);
                RemovePropW(window, chatScaleSizeProperty);
                chatWindow = nullptr;
                chatProcedure = nullptr;
                chatNativeWidth = 0;
            }
            else
            {
                RemovePropW(window, readyProperty);
                RemovePropW(window, widthProperty);
                RemovePropW(window, sizeProperty);
                AradGameSettings::ClearViewport();
                gameWindow = nullptr;
                gameProcedure = nullptr;
            }
        }
        return CallWindowProcW(previous, window, message, wParam, lParam);
    }

    static bool SubclassViewport(HWND window, WNDPROC& procedure)
    {
        procedure = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window, GWLP_WNDPROC));
        return procedure && SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&ViewportProcedure));
    }

    static void Attach(HWND window)
    {
        RECT client;
        if (!installed || !window || window == gameWindow || window == chatWindow || !IsWindowVisible(window) ||
            GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId() ||
            (GetWindowLongW(window, GWL_STYLE) & WS_CHILD) ||
            !GetClientRect(window, &client) || client.bottom != gameHeight)
            return;

        if (!gameWindow && client.right == gameWidth && SubclassViewport(window, gameProcedure))
        {
            gameWindow = window;
            AradGameSettings::TrackViewport(window);
            SetPropW(window, readyProperty, reinterpret_cast<HANDLE>(1));
        }
        else if (!chatWindow && client.right > 0 && client.right < gameWidth)
        {
            wchar_t cls[32];
            if (GetClassNameW(window, cls, ARRAYSIZE(cls)) && !wcscmp(cls, L"DNF_WND_CHAT") &&
                SubclassViewport(window, chatProcedure))
            {
                chatWindow = window;
                chatNativeWidth = client.right;
                SetPropW(window, chatNativeWidthProperty, ULongToHandle(chatNativeWidth));
            }
        }
    }

    static LRESULT WINAPI DispatchGameMessage(const MSG* message)
    {
        if (message && message == handledBindingMessage)
        {
            handledBindingMessage = nullptr;
            return 0;
        }
        // The game settles its window size after creating it. Subclass on its
        // own UI thread once it dispatches messages for the native viewport.
        if (message && (!gameWindow || !chatWindow)) Attach(message->hwnd);
        return dispatchMessage(message);
    }

    static BOOL WINAPI TranslateGameMessage(const MSG* message)
    {
        handledBindingMessage = nullptr;
        // Recover IME-processed keys before TranslateMessage discards the original virtual key.
        if (message && gameWindow && (message->hwnd == gameWindow || IsChild(gameWindow, message->hwnd)) &&
            AradGameSettings::CaptureQueuedKey(*message))
        {
            handledBindingMessage = message;
            return TRUE;
        }
        return translateMessage(message);
    }

    // Scale input only for the game's main viewport and detached chat window.
    static bool Install(BYTE* image)
    {
        auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(image + reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
        auto rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
        auto import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image + rva);
        HMODULE user = GetModuleHandleW(L"user32.dll");
        dispatchMessage = reinterpret_cast<decltype(dispatchMessage)>(GetProcAddress(user, "DispatchMessageW"));
        translateMessage = reinterpret_cast<decltype(translateMessage)>(GetProcAddress(user, "TranslateMessage"));
        screenToClient = reinterpret_cast<decltype(screenToClient)>(GetProcAddress(user, "ScreenToClient"));
        clientToScreen = reinterpret_cast<decltype(clientToScreen)>(GetProcAddress(user, "ClientToScreen"));
        if (!dispatchMessage || !translateMessage || !screenToClient || !clientToScreen || !InstallFramePresentation(image))
            return false;

        unsigned found = 0;
        for (; import->Name; ++import)
        {
            if (_stricmp(reinterpret_cast<char*>(image + import->Name), "USER32.dll") || !import->OriginalFirstThunk)
                continue;
            auto name = reinterpret_cast<IMAGE_THUNK_DATA*>(image + import->OriginalFirstThunk);
            auto slot = reinterpret_cast<IMAGE_THUNK_DATA*>(image + import->FirstThunk);
            for (; name->u1.AddressOfData; ++name, ++slot)
            {
                if (IMAGE_SNAP_BY_ORDINAL(name->u1.Ordinal)) continue;
                const char* function = reinterpret_cast<const char*>(
                    reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(image + name->u1.AddressOfData)->Name);
                void* replacement = nullptr;
                unsigned bit = 0;
                if (!strcmp(function, "DispatchMessageW")) { replacement = reinterpret_cast<void*>(&DispatchGameMessage); bit = 1; }
                if (!strcmp(function, "ScreenToClient")) { replacement = reinterpret_cast<void*>(&GameScreenToClient); bit = 2; }
                if (!strcmp(function, "ClientToScreen")) { replacement = reinterpret_cast<void*>(&GameClientToScreen); bit = 4; }
                if (!strcmp(function, "TranslateMessage")) { replacement = reinterpret_cast<void*>(&TranslateGameMessage); bit = 8; }
                if (!replacement) continue;
                DWORD protection, ignored;
                if (!VirtualProtect(&slot->u1.Function, sizeof(void*), PAGE_READWRITE, &protection)) return false;
                InterlockedExchangePointer(reinterpret_cast<void**>(&slot->u1.Function), replacement);
                VirtualProtect(&slot->u1.Function, sizeof(void*), protection, &ignored);
                found |= bit;
            }
        }
        installed = found == 15;
        return installed;
    }
}
