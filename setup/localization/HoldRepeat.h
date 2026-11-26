// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <cstdint>

// Shape the single held action in the game's input snapshot. Text messages,
// other keys and the Windows input queue are never generated or replayed.
class HoldRepeat
{
    unsigned binding = 0;
    bool waitingForRelease = true, held = false, output = false;
    std::uint32_t next = 0;

public:
    void Suspend() { waitingForRelease = true; held = output = false; }
    bool Shaping() const { return held; }
    bool WasDown() const { return output; }

    bool Sample(unsigned setting, bool eligible, bool down, std::uint32_t now)
    {
        if (setting != binding || !eligible)
        {
            binding = setting;
            Suspend();
        }
        if (!down)
        {
            waitingForRelease = false;
            held = output = false;
            return false;
        }
        const unsigned rate = setting >> 8;
        if (!eligible || !rate || waitingForRelease) return true;
        if (!held)
        {
            held = output = true;
            next = now + 260;
        }
        else if (static_cast<std::int32_t>(now - next) >= 0)
        {
            output = !output;
            next = now + 500 / rate;
        }
        return output;
    }
};
