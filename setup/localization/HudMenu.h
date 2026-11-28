// Copyright (c) 2026 eirros. All rights reserved.
#pragma once

namespace AradHudMenu
{
    using InitializePopup = HRESULT (__thiscall*)(BYTE*, const BYTE*, int, int, int, void*, void*);
    using GetImageFrame = void* (__thiscall*)(const BYTE*, unsigned int);
    using DrawHints = void (__thiscall*)(void*);

    constexpr size_t menuCount = 5;
    constexpr size_t visible = 0x10, enabled = 0x11;
    constexpr unsigned int menuBackingFrame = 172; // Appended to WindowCommon.img at package build time.
    static BYTE* client;
    static InitializePopup originalInitializePopup;
    static DrawHints originalDrawHints;

    template <typename T>
    static T Field(const BYTE* object, size_t offset)
    {
        return *reinterpret_cast<const T*>(object + offset);
    }

    static bool PopupOpen()
    {
        const auto navigation = Field<const BYTE*>(client, 0xdb1644);
        if (!navigation) return false;
        const auto popups = reinterpret_cast<const BYTE* const*>(navigation + 0x1d1c);
        for (size_t i = 0; i < menuCount; ++i)
        {
            const auto popup = popups[i];
            if (popup && popup[visible] && popup[enabled])
                return true;
        }
        return false;
    }

    static HRESULT __fastcall InitializeNavigationPopup(BYTE* panel, void*, const BYTE* image,
        int firstFrame, int lastFrame, int flags, void* parent, void* context)
    {
        const bool navigation = _ReturnAddress() == client + 0x5d59ce;
        const HRESULT status = originalInitializePopup(panel, image, firstFrame, lastFrame, flags, parent, context);
        if (!navigation || FAILED(status) || !image) return status;

        const auto begin = Field<UINT_PTR>(image, 0x54), end = Field<UINT_PTR>(image, 0x58);
        constexpr size_t frameEntrySize = 16;
        if (begin && end >= begin && end - begin == (menuBackingFrame + 1) * frameEntrySize)
        {
            const auto frame = reinterpret_cast<GetImageFrame>(client + 0x879cd0);
            // Swap the center tile on these five panels. The eight border tiles and row sprites still draw normally.
            if (const auto backing = frame(image, menuBackingFrame))
                *reinterpret_cast<void**>(panel + 0x8c) = backing;
        }
        return status;
    }

    static void __fastcall DrawToolbarHints(void* hud, void*)
    {
        if (!PopupOpen()) originalDrawHints(hud);
    }

    static bool Install(BYTE* image)
    {
        BYTE* popup = image + 0x882a30;
        BYTE* hints = image + 0x6e7d00;
        const BYTE popupCode[] = {0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x8a, 0x46, 0x04};
        const BYTE callerCode[] = {0xe8, 0x62, 0xd0, 0x2a, 0x00};
        const BYTE hintsCode[] = {0x55, 0x8b, 0xec, 0x51, 0x53, 0x56, 0x33, 0xd2};
        if (memcmp(popup, popupCode, sizeof(popupCode)) || memcmp(hints, hintsCode, sizeof(hintsCode)) ||
            memcmp(image + 0x5d59c9, callerCode, sizeof(callerCode)))
            return false;
        client = image;
        if (MH_CreateHook(popup, &InitializeNavigationPopup, reinterpret_cast<void**>(&originalInitializePopup)) != MH_OK)
            return false;
        if (MH_CreateHook(hints, &DrawToolbarHints, reinterpret_cast<void**>(&originalDrawHints)) != MH_OK)
        {
            MH_RemoveHook(popup);
            return false;
        }
        if (MH_QueueEnableHook(popup) == MH_OK && MH_QueueEnableHook(hints) == MH_OK && MH_ApplyQueued() == MH_OK)
            return true;
        MH_RemoveHook(hints);
        MH_RemoveHook(popup);
        return false;
    }
}
