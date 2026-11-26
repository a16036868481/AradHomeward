// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <windowsx.h>
#include "GameSettingsBridge.h"

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
    static BYTE* clientImage;
    static decltype(&DispatchMessageW) dispatchMessage;
    static decltype(&TranslateMessage) translateMessage;
    static const MSG* handledBindingMessage;
    static decltype(&ScreenToClient) screenToClient;
    static decltype(&ClientToScreen) clientToScreen;
    using DevicePresent = HRESULT (WINAPI*)(void*, const RECT*, const RECT*, HWND, const RGNDATA*);
    static DevicePresent originalPresent;
    static bool ScaledClient(HWND window, RECT& client, LONG& nativeWidth)
    {
        if (!window || (window != gameWindow && window != chatWindow)) return false;
        if (window == chatWindow)
        {
            auto requested = HandleToULong(GetPropW(window, chatScaleSizeProperty));
            nativeWidth = chatNativeWidth;
            return requested && GetClientRect(window, &client) &&
                client.right == LOWORD(requested) && client.bottom == HIWORD(requested);
        }
        nativeWidth = gameWidth;
        auto size = HandleToULong(GetPropW(window, sizeProperty));
        if (size)
            return GetClientRect(window, &client) && client.right == LOWORD(size) && client.bottom == HIWORD(size);
        if (GetPropW(window, AradGameSettings::screenModeProperty) == ULongToHandle(AradGameSettings::maximized + 1))
        {
            // The client's maximized mode stretches the 640 x 480 canvas to
            // the desktop, but its mouse handler still expects canvas coordinates.
            return GetClientRect(window, &client) && client.right > 0 && client.bottom > 0 &&
                (client.right != gameWidth || client.bottom != gameHeight);
        }
        auto requested = reinterpret_cast<LONG_PTR>(GetPropW(window, widthProperty));
        // While the display is changing, keep the original coordinates until
        // both sides agree on the actual client size.
        return requested > gameWidth && GetClientRect(window, &client) &&
            client.right == requested && client.right * gameHeight == client.bottom * gameWidth;
    }

    static void ToNativePoint(POINT& point, const RECT& client, LONG nativeWidth)
    {
        auto scale = [](LONG coordinate, LONG game, LONG actual) -> LONG
        {
            LONGLONG numerator = static_cast<LONGLONG>(coordinate) * game;
            return static_cast<LONG>((numerator < 0 ? numerator - actual + 1 : numerator) / actual);
        };
        point.x = scale(point.x, nativeWidth, client.right);
        point.y = scale(point.y, gameHeight, client.bottom);
    }

    static HRESULT WINAPI PresentChat(void* device, const RECT* source, const RECT* destination,
        HWND target, const RGNDATA* dirty)
    {
        RECT client;
        LONG nativeWidth;
        // The renderer draws chat at its native size in the shared back buffer.
        // Its full-window Present passes the resized client rect for both
        // arguments. Keep that destination and sample only the chat canvas.
        if (chatWindow && target == chatWindow && source && destination &&
            ScaledClient(target, client, nativeWidth) &&
            EqualRect(source, &client) && EqualRect(destination, &client))
        {
            const RECT native = { 0, 0, nativeWidth, gameHeight };
            return originalPresent(device, &native, destination, target, dirty);
        }
        return originalPresent(device, source, destination, target, dirty);
    }

    static bool InstallChatPresentation()
    {
        // Only the supported client's Direct3D8 renderer uses this layout.
        constexpr DWORD rendererGlobal = 0xE96470, rendererVtable = 0xBC6124;
        constexpr DWORD rendererPresent = 0x877460, deviceOffset = 0x203c;
        const auto renderer = *reinterpret_cast<BYTE* const*>(clientImage + rendererGlobal);
        if (!renderer || *reinterpret_cast<BYTE* const*>(renderer) != clientImage + rendererVtable ||
            *reinterpret_cast<BYTE* const*>(clientImage + rendererVtable + 8) != clientImage + rendererPresent)
            return false;
        const auto device = *reinterpret_cast<void** const*>(renderer + deviceOffset);
        if (!device) return false;
        auto slot = reinterpret_cast<DevicePresent*>(device[0]) + 15; // IDirect3DDevice8::Present
        if (*slot == &PresentChat) return true;
        if (!*slot || (originalPresent && *slot != originalPresent)) return false;
        DWORD protection, ignored;
        if (!VirtualProtect(slot, sizeof(*slot), PAGE_READWRITE, &protection)) return false;
        originalPresent = *slot;
        InterlockedExchangePointer(reinterpret_cast<void**>(slot), reinterpret_cast<void*>(&PresentChat));
        VirtualProtect(slot, sizeof(*slot), protection, &ignored);
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
            point->x = MulDiv(point->x, client.right, nativeWidth);
            point->y = MulDiv(point->y, client.bottom, gameHeight);
        }
        return clientToScreen(window, point);
    }

    static LRESULT CALLBACK ViewportProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        const bool isChat = window == chatWindow;
        WNDPROC previous = isChat ? chatProcedure : gameProcedure;
        if (!isChat && message == WM_SIZE) AradGameSettings::PublishScreenMode(AradGameSettings::Config());
        if (isChat && message == WM_SIZE)
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
                InstallChatPresentation() && SubclassViewport(window, chatProcedure))
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
        clientImage = image;
        auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(image + reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
        auto rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
        auto import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image + rva);
        HMODULE user = GetModuleHandleW(L"user32.dll");
        dispatchMessage = reinterpret_cast<decltype(dispatchMessage)>(GetProcAddress(user, "DispatchMessageW"));
        translateMessage = reinterpret_cast<decltype(translateMessage)>(GetProcAddress(user, "TranslateMessage"));
        screenToClient = reinterpret_cast<decltype(screenToClient)>(GetProcAddress(user, "ScreenToClient"));
        clientToScreen = reinterpret_cast<decltype(clientToScreen)>(GetProcAddress(user, "ClientToScreen"));
        if (!dispatchMessage || !translateMessage || !screenToClient || !clientToScreen) return false;

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
