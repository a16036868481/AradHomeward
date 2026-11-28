// Copyright (c) 2026 eirros. All rights reserved.
#pragma once

namespace AradDungeonMinimap
{
    using DrawSprite = void (__thiscall*)(void*, int, int, void*);
    using DrawEffect = void (__thiscall*)(void*, int, int, void*, float, float, float, DWORD, float, float);
    using GetIndexedSprite = void* (__thiscall*)(void*, int);
    using SetVisible = void (__thiscall*)(void*, bool);

    constexpr size_t drawSprite = 0x89d4f0;
    constexpr size_t drawEffect = 0x89d5e0;
    constexpr int minimapImage = 17;
    constexpr int headingFrame = 170; // Appended by repair_shared_sprites.py.
    static BYTE* client;

    template <typename T>
    static T Field(const BYTE* object, size_t offset)
    {
        return *reinterpret_cast<const T*>(object + offset);
    }

    static bool ExpandedMapVisible()
    {
        const auto gameUi = Field<const BYTE*>(client, 0xdb1644);
        if (!gameUi) return false;
        // The dungeon map toggle opens and closes window 0x62. The window list
        // also tracks the title-bar close button, which can leave the key flag set.
        const auto begin = Field<const BYTE*>(gameUi, 0x724);
        return begin && begin != Field<const BYTE*>(gameUi, 0x728);
    }

    static void __fastcall DrawCircularSprite(void* renderer, void*, int x, int y, void* sprite)
    {
        if (!ExpandedMapVisible())
            reinterpret_cast<DrawSprite>(client + drawSprite)(renderer, x, y, sprite);
    }

    static void __fastcall DrawCircularEffect(void* renderer, void*, int x, int y, void* sprite,
        float scaleX, float scaleY, float angle, DWORD color, float originX, float originY)
    {
        if (!ExpandedMapVisible())
            reinterpret_cast<DrawEffect>(client + drawEffect)(
                renderer, x, y, sprite, scaleX, scaleY, angle, color, originX, originY);
    }

    static void __fastcall DrawHeading(void* renderer, void*, int x, int y, void* sprite)
    {
        if (ExpandedMapVisible())
        {
            const auto images = Field<void*>(client, 0xdabbc0);
            const auto getImage = reinterpret_cast<GetIndexedSprite>(client + 0x87ef40);
            const auto getFrame = reinterpret_cast<GetIndexedSprite>(client + 0x879cd0);
            const auto image = images ? getImage(images, minimapImage) : nullptr;
            sprite = image ? getFrame(image, headingFrame) : nullptr;
        }
        reinterpret_cast<DrawSprite>(client + drawSprite)(renderer, x, y, sprite);
    }

    static void __fastcall UpdateCollapsedButton(void* button, void*)
    {
        // Use the original visibility setter so hidden map buttons also lose their hit targets.
        (*static_cast<SetVisible**>(button))[4](button, !ExpandedMapVisible());
    }

    static bool Install(BYTE* image)
    {
        // Redirect just the circular dungeon map artwork. The title text and
        // the adjacent item counter still go through their original draw calls.
        struct SpriteCall { size_t offset; size_t original; void* replacement; };
        const auto circle = reinterpret_cast<void*>(&DrawCircularSprite);
        const auto effect = reinterpret_cast<void*>(&DrawCircularEffect);
        const SpriteCall sprites[] = {
            {0x6df9e6, drawSprite, circle}, // Map background.
            {0x6dfa90, drawEffect, effect},
            {0x6dfb63, drawSprite, circle}, // Rooms and markers.
            {0x6dfce7, drawSprite, circle},
            {0x6dfd39, drawEffect, effect},
            {0x6dfdfb, drawEffect, effect},
            {0x6dfe30, drawSprite, reinterpret_cast<void*>(&DrawHeading)},
            {0x6dfe59, drawSprite, circle}, // Player marker.
            {0x6e0025, drawSprite, circle}, // Room effect badge.
            {0x6e00dc, drawSprite, circle},
            {0x6e012f, drawSprite, circle},
            {0x6e0183, drawSprite, circle},
            {0x6e01d4, drawSprite, circle},
            {0x6e0220, drawEffect, effect},
        };
        BYTE* lens = image + 0x6e923b;
        BYTE* cornerButton = image + 0x6e924e;
        const BYTE lensCode[] = {0x6a, 0x01, 0xff, 0x50, 0x10};
        const BYTE cornerCode[] = {0x6a, 0x01, 0xff, 0x52, 0x10};
        using AradGameSettings::IsCall;
        using AradGameSettings::ReplaceCall;
        for (const auto& site : sprites)
            if (!IsCall(image + site.offset, image + site.original)) return false;
        if (memcmp(lens, lensCode, sizeof(lensCode)) ||
            memcmp(cornerButton, cornerCode, sizeof(cornerCode)))
            return false;

        BYTE* draw = image + sprites[0].offset;
        const size_t drawBytes = sprites[_countof(sprites) - 1].offset - sprites[0].offset + 5;
        const size_t buttonBytes = cornerButton + 5 - lens;
        DWORD drawProtection, buttonProtection, ignored;
        if (!VirtualProtect(draw, drawBytes, PAGE_EXECUTE_READWRITE, &drawProtection)) return false;
        if (!VirtualProtect(lens, buttonBytes, PAGE_EXECUTE_READWRITE, &buttonProtection))
        {
            VirtualProtect(draw, drawBytes, drawProtection, &ignored);
            return false;
        }
        client = image;
        for (const auto& site : sprites)
            ReplaceCall(image + site.offset, site.replacement);
        *lens = *cornerButton = 0xe8;
        ReplaceCall(lens, reinterpret_cast<void*>(&UpdateCollapsedButton));
        ReplaceCall(cornerButton, reinterpret_cast<void*>(&UpdateCollapsedButton));
        VirtualProtect(lens, buttonBytes, buttonProtection, &ignored);
        VirtualProtect(draw, drawBytes, drawProtection, &ignored);
        FlushInstructionCache(GetCurrentProcess(), draw, drawBytes);
        FlushInstructionCache(GetCurrentProcess(), lens, buttonBytes);
        return true;
    }
}
