// Copyright (c) 2026 eirros. All rights reserved.
#pragma once

constexpr float ScaleTownSpeed(float speed, unsigned setting)
{
    // The window property stores percent + 1. An absent or invalid setting uses full speed.
    if (!setting || setting >= 101) return speed;
    const float multiplier = static_cast<float>(setting - 1) / 100.0f;
    return speed * multiplier;
}
