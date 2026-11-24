// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <windows.h>
#include <windowsx.h>

namespace AradWindowInput
{
    constexpr LONG gameWidth = 640;
    constexpr LONG gameHeight = 480;
    constexpr wchar_t readyProperty[] = L"AradHomeward.WindowInputReady";
    constexpr wchar_t widthProperty[] = L"AradHomeward.WindowScaleWidth";

    static HWND gameWindow;
    static WNDPROC gameProcedure;
    static bool installed;
    static decltype(&DispatchMessageW) dispatchMessage;
    static decltype(&ScreenToClient) screenToClient;
    static decltype(&ClientToScreen) clientToScreen;

    static bool ScaledClient(HWND window, RECT& client)
    {
        if (window != gameWindow || !window) return false;
        auto requested = reinterpret_cast<LONG_PTR>(GetPropW(window, widthProperty));
        // While the display is changing, keep the original coordinates until
        // both sides agree on the actual client size.
        return requested > gameWidth && GetClientRect(window, &client) &&
            client.right == requested && client.right * gameHeight == client.bottom * gameWidth;
    }

    static void ToGamePoint(POINT& point, const RECT& client)
    {
        auto scale = [](LONG coordinate, LONG game, LONG actual) -> LONG
        {
            LONGLONG numerator = static_cast<LONGLONG>(coordinate) * game;
            return static_cast<LONG>((numerator < 0 ? numerator - actual + 1 : numerator) / actual);
        };
        point.x = scale(point.x, gameWidth, client.right);
        point.y = scale(point.y, gameHeight, client.bottom);
    }

    static BOOL WINAPI GameScreenToClient(HWND window, LPPOINT point)
    {
        BOOL ok = screenToClient(window, point);
        RECT client;
        if (ok && ScaledClient(window, client)) ToGamePoint(*point, client);
        return ok;
    }

    static BOOL WINAPI GameClientToScreen(HWND window, LPPOINT point)
    {
        RECT client;
        if (point && ScaledClient(window, client))
        {
            point->x = MulDiv(point->x, client.right, gameWidth);
            point->y = MulDiv(point->y, client.bottom, gameHeight);
        }
        return clientToScreen(window, point);
    }

    static LRESULT CALLBACK GameProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        WNDPROC previous = gameProcedure;
        if (message >= WM_MOUSEFIRST && message <= WM_MOUSELAST &&
            message != WM_MOUSEWHEEL && message != WM_MOUSEHWHEEL)
        {
            RECT client;
            if (ScaledClient(window, client))
            {
                POINT point = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                ToGamePoint(point, client);
                lParam = MAKELPARAM(point.x, point.y);
            }
        }
        if (message == WM_NCDESTROY)
        {
            RemovePropW(window, readyProperty);
            RemovePropW(window, widthProperty);
            gameWindow = nullptr;
            gameProcedure = nullptr;
        }
        return CallWindowProcW(previous, window, message, wParam, lParam);
    }

    static void Attach(HWND window)
    {
        RECT client;
        if (!installed || gameWindow || !window || !IsWindowVisible(window) ||
            GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId() ||
            (GetWindowLongW(window, GWL_STYLE) & WS_CHILD) ||
            !GetClientRect(window, &client) || client.right != gameWidth || client.bottom != gameHeight)
            return;

        gameProcedure = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window, GWLP_WNDPROC));
        if (gameProcedure && SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&GameProcedure)))
        {
            gameWindow = window;
            SetPropW(window, readyProperty, reinterpret_cast<HANDLE>(1));
        }
    }

    static LRESULT WINAPI DispatchGameMessage(const MSG* message)
    {
        // The game settles its window size after creating it. Subclass on its
        // own UI thread once it dispatches messages for the native viewport.
        if (!gameWindow && message) Attach(message->hwnd);
        return dispatchMessage(message);
    }

    // Limit coordinate virtualization to the game's own imports and main
    // window. Chat, desktop mouse coordinates and the original size stay native.
    static bool Install(BYTE* image)
    {
        auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(image + reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
        auto rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
        auto import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image + rva);
        HMODULE user = GetModuleHandleW(L"user32.dll");
        dispatchMessage = reinterpret_cast<decltype(dispatchMessage)>(GetProcAddress(user, "DispatchMessageW"));
        screenToClient = reinterpret_cast<decltype(screenToClient)>(GetProcAddress(user, "ScreenToClient"));
        clientToScreen = reinterpret_cast<decltype(clientToScreen)>(GetProcAddress(user, "ClientToScreen"));
        if (!dispatchMessage || !screenToClient || !clientToScreen) return false;

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
                if (!replacement) continue;
                DWORD protection, ignored;
                if (!VirtualProtect(&slot->u1.Function, sizeof(void*), PAGE_READWRITE, &protection)) return false;
                InterlockedExchangePointer(reinterpret_cast<void**>(&slot->u1.Function), replacement);
                VirtualProtect(&slot->u1.Function, sizeof(void*), protection, &ignored);
                found |= bit;
            }
        }
        installed = found == 7;
        return installed;
    }
}
