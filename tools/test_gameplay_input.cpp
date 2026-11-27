#include "../setup/localization/HoldRepeat.h"
#include "../setup/localization/TownPace.h"
#include <cassert>
#include <cstdlib>
#include <initializer_list>

int main()
{
    for (unsigned rate : {4u, 8u, 12u})
    {
        HoldRepeat key;
        const auto setting = rate * 256 + 'X';
        const std::uint32_t start = 0xfffffff0u;
        assert(!key.Sample(setting, true, false, start));
        bool previous = false;
        unsigned edges = 0;
        for (unsigned dt = 1; dt <= 2250; dt += 2)
        {
            bool down = key.Sample(setting, true, true, start + dt);
            if (down && !previous) ++edges;
            if (dt < 260) assert(down);
            previous = down;
        }
        assert(std::abs(static_cast<int>(edges) - static_cast<int>(2 * rate + 1)) <= 1);
        key.Suspend();
        for (unsigned dt = 0; dt < 500; dt += 12)
        {
            key.Sample(setting, true, true, start + 2300 + dt);
            assert(!key.Shaping());
        }
        assert(!key.Sample(setting, true, false, start + 2900));
        assert(key.Sample(setting, true, true, start + 3010) && key.Shaping());
        key.Sample(setting, false, true, start + 3011);
        assert(!key.Shaping());
    }
    const float nativeMultiplier = 1.25f;
    for (unsigned rate = 0; rate <= 100; ++rate)
    {
        const float expected = nativeMultiplier * rate / 100.0f;
        assert(std::abs(ScaleTownSpeed(nativeMultiplier, rate + 1) - expected) < 0.00001f);
        assert(std::abs(ScaleTownSpeed(-nativeMultiplier, rate + 1) + expected) < 0.00001f);
        assert(ScaleTownSpeed(0.0f, rate + 1) == 0.0f);
    }
    for (unsigned setting : {0u, 125u})
        assert(ScaleTownSpeed(nativeMultiplier, setting) == nativeMultiplier);
}
