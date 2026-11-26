// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <cstdint>

// Share a single time budget across axes so diagonal movement keeps its direction.
class TownPace
{
    unsigned rate = 0;
    bool moving = false, forwarded = false;
    std::uint32_t last = 0;
    int credit = 0;
public:
    void Reset() { moving = false; }
    bool Sample(unsigned percent, bool down, std::uint32_t now)
    {
        if (!down || percent == 0 || percent >= 100) { Reset(); return true; }
        const std::uint32_t elapsed = now - last;
        if (!moving || percent != rate || elapsed > 100)
        {
            moving = forwarded = true;
            rate = percent;
            credit = 0;
        }
        else if (elapsed)
        {
            credit += static_cast<int>(elapsed) * (static_cast<int>(percent) - (forwarded ? 100 : 0));
            forwarded = credit >= 0;
        }
        last = now;
        return forwarded;
    }
};
