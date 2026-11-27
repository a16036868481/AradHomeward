// Copyright (c) 2026 eirros. All rights reserved.
#pragma once

namespace AradTownOwl
{
    using ItemUseCheck = int (__thiscall*)(BYTE*);
    using SceneNumber = int (__thiscall*)(void*);
    using WarehouseUnavailable = bool (__thiscall*)(void*);
    using OpenWindow = void* (__thiscall*)(void*, int, void*, int);
    using CooldownRemaining = int (__thiscall*)(BYTE*);
    using StartCooldown = void (__thiscall*)(BYTE*, int);

    constexpr int owlItemId = 51;
    constexpr int warehouseWindow = 0x1c;
    constexpr int townScene = 1;
    constexpr int owlCooldownMs = 300000;

    struct OpenWindows
    {
        void* const* first;
        void* const* end;
    };

    static BYTE* client;
    static ItemUseCheck originalItemUseCheck;

    static int __fastcall CheckItemUse(BYTE* item, void*)
    {
        // This call belongs to the shared item-use action for the bag and quick bar.
        // Keep the original result so a town use cannot consume an item or spawn a dungeon actor.
        const bool usingItem = _ReturnAddress() == client + 0x134F618;
        const auto original = originalItemUseCheck(item);
        if (!usingItem || original || *reinterpret_cast<const BYTE* const*>(item) != client + 0xA0C89C ||
            *reinterpret_cast<const int*>(item + 0x10) != owlItemId)
            return original;

        auto scene = *reinterpret_cast<BYTE* const*>(client + 0xDB0830);
        auto windows = *reinterpret_cast<BYTE* const*>(client + 0xDB1644);
        auto townPlayer = *reinterpret_cast<void* const*>(client + 0xE07E18);
        if (!scene || !windows || !townPlayer ||
            reinterpret_cast<SceneNumber>(client + 0x8A58A0)(scene + 0x18) != townScene ||
            reinterpret_cast<WarehouseUnavailable>(client + 0x4D670)(scene) ||
            reinterpret_cast<CooldownRemaining>(client + 0x9C2A0)(item) > 0)
            return original;

        const auto& open = *reinterpret_cast<const OpenWindows*>(windows + 0x104 + 0x10 * warehouseWindow);
        if (open.first != open.end) return original;

        // Reuse the same window and availability check as the town chest. The
        // native warehouse still handles all inventory access and transfers.
        if (reinterpret_cast<OpenWindow>(client + 0x5CF1D0)(windows, warehouseWindow, nullptr, 0))
            reinterpret_cast<StartCooldown>(client + 0x90CD0)(item, owlCooldownMs);
        return original;
    }

    static bool Install(BYTE* image)
    {
        const BYTE useCheck[] = {0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68};
        const BYTE checkTownChest[] = {0x8B, 0x0D, 0x30, 0x08, 0x1B, 0x01, 0xE8, 0x9D, 0x56, 0xF9, 0xFF};
        const BYTE nativeCooldown[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x18, 0x53, 0x56, 0x8B, 0xF1};
        auto target = image + 0x8ED80;
        if (memcmp(target, useCheck, sizeof(useCheck)) ||
            memcmp(image + 0xB7FC8, checkTownChest, sizeof(checkTownChest)) ||
            memcmp(image + 0x90CD0, nativeCooldown, sizeof(nativeCooldown)))
            return false;

        client = image;
        if (MH_CreateHook(target, reinterpret_cast<void*>(&CheckItemUse),
            reinterpret_cast<void**>(&originalItemUseCheck)) != MH_OK) return false;
        if (MH_EnableHook(target) == MH_OK) return true;
        MH_RemoveHook(target);
        return false;
    }
}
