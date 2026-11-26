// Copyright (c) 2026 eirros. All rights reserved.
#pragma once
#include <cstring>
#include <cwchar>
#include <intrin.h>
#include <windows.h>
#include "HoldRepeat.h"
#include "TownPace.h"

namespace AradGameSettings
{
    constexpr wchar_t endpointProperty[] = L"AradHomeward.GamePreferences";
    constexpr wchar_t screenModeProperty[] = L"AradHomeward.NativeScreenMode";
    constexpr ULONG_PTR packet = 0x41524832;
    constexpr UINT sendFlags = SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT;
    constexpr int windowed = 0, maximized = 1, fullScreen = 2, nativeScreenOption = 4;
    constexpr int gameSettingsTab = 1, bindingCount = 22, defaultBinding = 0x100;
    constexpr int baseFields = bindingCount + 3, actionFields = baseFields + 3;
    constexpr int townRow = 44, repeatRow = 87, frequencyRow = 119, keyRow = 151, padRow = 215;
    constexpr int townSpeeds[] = {100, 75, 50, 25};
    constexpr int repeatRates[] = {4, 8, 12};
    constexpr int legacyRepeatRates[] = {0, 4, 8, 12};
    constexpr int scales[] = {100, 125, 150, 175, 200, 210, 225, 250, 275, 300, 320, 400, 450, 0};
    constexpr DWORD normalInk = 0xffe9d9b3, selectedInk = 0xffffe57b;

    struct Preferences
    {
        int display, scale, padEnabled, keys[bindingCount], townSpeed, repeatRate, repeatKey, repeatEnabled;
    };

    static_assert(sizeof(Preferences) == (actionFields + 1) * sizeof(int), "Game preferences packet");

    struct Labels
    {
        const wchar_t *game, *pad, *size, *fit, *enabled, *on, *off, *defaults, *hint, *pressKey, *saveError;
    };

    constexpr Labels localized[] =
    {
        {L"游戏", L"手柄", L"窗口大小", L"适应屏幕", L"手柄支持", L"开启", L"关闭", L"恢复默认",
            L"点选键位。Delete 清空", L"请按键盘键", L"设置保存失败，请重试"},
        {L"遊戲", L"手把", L"視窗大小", L"配合螢幕", L"手把支援", L"開啟", L"關閉", L"還原預設",
            L"點選按鍵。Delete 清空", L"請按鍵盤鍵", L"設定儲存失敗，請重試"},
        {L"Game", L"Gamepad", L"Window size", L"Fit to screen", L"Gamepad", L"On", L"Off", L"Defaults",
            L"Select a key. Delete clears it.", L"Press a keyboard key", L"Could not save settings. Try again."},
        {L"ゲーム", L"パッド", L"画面サイズ", L"画面に合わせる", L"パッドを使用", L"オン", L"オフ", L"初期設定",
            L"キーを選択。Delete で解除", L"キーを押してください", L"保存に失敗しました"},
        {L"게임", L"패드", L"창 크기", L"화면에 맞춤", L"패드 사용", L"켜기", L"끄기", L"기본값",
            L"키 선택. Delete로 해제", L"키보드 키를 누르세요", L"설정을 저장하지 못했습니다"}
    };
    constexpr const wchar_t* padActions[bindingCount] =
    {
        L"←", L"→", L"↑", L"↓", L"X", L"A", L"Y", L"B",
        L"LB+X", L"LB+A", L"LB+Y", L"LB+B",
        L"RB+X", L"RB+A", L"RB+Y", L"RB+B",
        L"LB+RB X", L"LB+RB A", L"LB+RB Y", L"LB+RB B", L"Start", L"Back"
    };

    struct ActionLabels { const wchar_t *controls, *actions, *town, *repeat, *frequency, *key, *keyHint; };
    constexpr ActionLabels actionLabels[] = {
        {L"操作", L"角色操作", L"城镇移动速度", L"按住连发", L"连发频率", L"连发键", L"请选择字母键或数字键"},
        {L"操作", L"角色操作", L"城鎮移動速度", L"按住連發", L"連發頻率", L"連發鍵", L"請選擇字母鍵或數字鍵"},
        {L"Controls", L"Actions", L"Town movement", L"Hold to repeat", L"Repeat rate", L"Repeat key", L"Press a letter or number key"},
        {L"操作", L"キャラ操作", L"街での移動速度", L"長押し連射", L"連射頻度", L"連射キー", L"英字か数字キーを押してください"},
        {L"조작", L"캐릭터 조작", L"마을 이동 속도", L"누르고 연타", L"연타 빈도", L"연타 키", L"영문 또는 숫자 키를 누르세요"}
    };
    enum class Page { Game, Actions, Pad };

    using OpenWindow = bool (__thiscall*)(void*, int, int);
    using DrawWindow = void (__thiscall*)(void*, void*);
    using UpdateWindow = void (__thiscall*)(void*);
    using DestroyWindow = void* (__thiscall*)(void*, unsigned);
    using PollKeyboard = HRESULT (__thiscall*)(void*);
    using ReadImeKey = UINT (WINAPI*)(HWND);
    using ReadOption = int (__thiscall*)(void*, int);
    using WriteOption = bool (__thiscall*)(void*, int, int);
    using NativeUpdateDisplay = void (__cdecl*)(int);
    using SetPosition = void (__thiscall*)(void*, int, int);
    using SetVisibility = void (__thiscall*)(void*, bool);
    using Allocate = void* (__cdecl*)(unsigned);
    using AttachControl = int (__thiscall*)(void*, void*);
    using BuildControl = void* (__thiscall*)(void*);
    using BuildButton = void* (__thiscall*)(void*, void*, int, int, int, int, int, int);
    using GetSkin = void* (__thiscall*)(void*, int);
    using BuildCombo = void* (__thiscall*)(void*, int, void*, void*, int, void*, int, int, int, int, int, int, int);
    using ComboItem = int (__thiscall*)(void*, const wchar_t*, int, int, DWORD, DWORD, DWORD, DWORD);
    using SelectEntry = void (__thiscall*)(void*, int);
    struct FontStyle { BYTE outline, flags; WORD width, lines; BYTE alignment, lineSpacing; void* font; };
    static_assert(sizeof(FontStyle) == 12, "Native options font style");
    using SetFontStyle = void (__thiscall*)(void*, const FontStyle*);
    using RestoreFontStyle = void (__thiscall*)(void*);
    using DrawInlineText = void (__thiscall*)(void*, int, int, DWORD, const wchar_t*);
    struct GameString { DWORD data[7]; };
    using MakeString = void (__thiscall*)(GameString*, const wchar_t*);
    using SetCaption = void (__thiscall*)(void*, GameString, DWORD, DWORD, DWORD, DWORD);

    struct SettingsControls
    {
        void* window;
        void* originalPane;
        void* padPane;
        void* actionPane;
        void* size;
        void* gameTab;
        void* padTab;
        void* enabled;
        void* defaults;
        void *townSpeed, *repeatSwitch, *repeatRate, *repeatKey, *openPad, *backToActions;
        void* bindings[bindingCount];
        Preferences values;
        bool ready, active, extended, canToggleRepeat, saveError;
        Page page;
        int binding;
    };

    static BYTE* image;
    static HWND viewport;
    static UINT readMessage;
    static unsigned capturedScan;
    static UINT queuedBindingKey;
    static unsigned queuedBindingScan;
    static ReadImeKey readImeKey;
    static SettingsControls ui;
    static const Labels* text = &localized[0];
    static const ActionLabels& Actions() { return actionLabels[text - localized]; }
    static HoldRepeat repeat;
    static TownPace pace;
    static void SuspendInput() { repeat.Suspend(); pace.Reset(); }
    static OpenWindow openOriginal;
    static DrawWindow drawOriginal;
    static UpdateWindow updateOriginal, closeOriginal, finishOriginal;
    static DestroyWindow destroyOriginal;
    static PollKeyboard pollOriginal;

    template <typename T> static T Address(DWORD address)
    {
        return reinterpret_cast<T>(image + (address - 0x400000));
    }

    template <typename T> static T& Field(void* object, size_t offset)
    {
        return *reinterpret_cast<T*>(static_cast<BYTE*>(object) + offset);
    }

    static void* Frame(void* window) { return Field<void*>(window, 8); }
    static int X(void* control) { return Field<int>(control, 0x14); }
    static int Y(void* control) { return Field<int>(control, 0x18); }

    static void* Config()
    {
        if (!image) return nullptr;
        const auto config = *Address<BYTE**>(0x11b1664);
        return config && Field<BYTE*>(config, 0) == Address<BYTE*>(0xdf4c54) && Field<void*>(config, 4) ? config : nullptr;
    }

    static int ScreenMode(void* config) { return Address<ReadOption>(0xbe4280)(config, nativeScreenOption); }

    static void PublishScreenMode(void* config)
    {
        if (!viewport || !config) return;
        const int mode = ScreenMode(config);
        if (mode >= windowed && mode <= fullScreen)
            SetPropW(viewport, screenModeProperty, ULongToHandle(mode + 1));
    }

    static void TrackViewport(HWND window)
    {
        viewport = window;
        PublishScreenMode(Config());
    }

    static void ClearViewport()
    {
        RemovePropW(viewport, screenModeProperty);
        ui.active = false;
        capturedScan = 0;
        queuedBindingKey = 0;
        queuedBindingScan = 0;
        SuspendInput();
        viewport = nullptr;
    }

    static HWND Endpoint()
    {
        return static_cast<HWND>(GetPropW(viewport, endpointProperty));
    }

    static int ReadPreference(HWND endpoint, int field)
    {
        DWORD_PTR result = 0;
        if (!SendMessageTimeoutW(endpoint, readMessage, reinterpret_cast<WPARAM>(viewport), field, sendFlags, 250, &result))
            return -1;
        return static_cast<int>(result) - 1;
    }

    static bool ReadPreferences()
    {
        const HWND endpoint = Endpoint();
        if (!endpoint || !IsWindow(endpoint)) return false;
        Preferences values = {ReadPreference(endpoint, 0), ReadPreference(endpoint, 1), ReadPreference(endpoint, 2), {}};
        if (values.display < windowed || values.display > fullScreen || values.scale < 0 || values.padEnabled < 0) return false;
        for (int i = 0; i < bindingCount; ++i)
        {
            values.keys[i] = ReadPreference(endpoint, i + 3);
            if (values.keys[i] < 0) return false;
        }
        values.townSpeed = ReadPreference(endpoint, baseFields);
        ui.extended = values.townSpeed >= 0;
        ui.canToggleRepeat = false;
        if (ui.extended)
        {
            values.repeatKey = ReadPreference(endpoint, baseFields + 2);
            values.repeatEnabled = ReadPreference(endpoint, actionFields);
            ui.canToggleRepeat = values.repeatEnabled >= 0 && values.repeatEnabled <= 1;
            // The legacy field returns zero for off. The new field retains the chosen rate.
            values.repeatRate = ReadPreference(endpoint, ui.canToggleRepeat ? actionFields + 1 : baseFields + 1);
            if (values.repeatRate < 0 || values.repeatKey < 0) return false;
        }
        if (auto config = Config())
        {
            const int mode = ScreenMode(config);
            if (mode > windowed && values.display == windowed && mode <= fullScreen) values.display = mode;
        }
        ui.values = values;
        return true;
    }

    static bool SavePreferences()
    {
        const int selected = Field<int>(ui.size, 0x88);
        if (selected >= 0 && selected < ARRAYSIZE(scales)) ui.values.scale = scales[selected];
        // The three original display choices share the scalable windowed renderer.
        auto config = Config();
        if (!config) return false;
        if (ScreenMode(config) != windowed && !Address<WriteOption>(0xbe4f30)(config, nativeScreenOption, windowed))
            return false;
        PublishScreenMode(config);
        const int fields = ui.canToggleRepeat ? actionFields + 1 : ui.extended ? actionFields : baseFields;
        COPYDATASTRUCT data = {packet, static_cast<DWORD>(fields * sizeof(int)), &ui.values};
        DWORD_PTR result = 0;
        const HWND endpoint = Endpoint();
        return endpoint && SendMessageTimeoutW(endpoint, WM_COPYDATA, reinterpret_cast<WPARAM>(viewport),
            reinterpret_cast<LPARAM>(&data), sendFlags, 3000, &result) && result == 1;
    }

    static void Move(void* control, int x, int y)
    {
        Address<SetPosition>(0xc9ae40)(control, x, y);
    }

    static void Visible(void* control, bool visible)
    {
        if (control) Field<SetVisibility*>(control, 0)[4](control, visible);
    }

    static void Caption(void* button, const wchar_t* title, DWORD color = normalInk)
    {
        GameString label = {};
        Address<MakeString>(0x403810)(&label, title);
        Address<SetCaption>(0x9e8040)(button, label, color, selectedInk, selectedInk, 0xff8c8c8c);
    }

    static void* NewButton(void* parent, const wchar_t* title, int x, int y)
    {
        auto storage = Address<Allocate>(0x401fb0)(0xd4);
        if (!storage) return nullptr;
        auto button = Address<BuildButton>(0x5a54e0)(storage, Field<void*>(ui.window, 0x64), 12, 13, 14, 15, 0, 0);
        Field<BYTE>(button, 0xbc) = 1;
        Field<void*>(button, 0x8c) = *Address<void**>(0x11abbc4);
        Caption(button, title);
        Move(button, x, y);
        Address<AttachControl>(0xc9afa0)(parent, button);
        return button;
    }

    static void* NewSizeCombo()
    {
        auto storage = Address<Allocate>(0x401fb0)(0xe4);
        if (!storage) return nullptr;
        auto comboSkin = Address<GetSkin>(0xc7ef40)(*Address<void**>(0x11abbc0), 9);
        // Open the list above the field so the original Save button stays accessible.
        auto combo = Address<BuildCombo>(0x9e7850)(storage, 1, Field<void*>(ui.window, 0x64), comboSkin, ARRAYSIZE(scales),
            *Address<void**>(0x11abbc4), 106, 0, 1, ARRAYSIZE(scales), 30, 1, 0);
        static wchar_t dimensions[ARRAYSIZE(scales)][16];
        for (int i = 0; i < ARRAYSIZE(scales); ++i)
        {
            const wchar_t* label = text->fit;
            if (scales[i] != 0)
            {
                swprintf_s(dimensions[i], L"%d x %d", 640 * scales[i] / 100, 480 * scales[i] / 100);
                label = dimensions[i];
            }
            Address<ComboItem>(0xc5ea30)(combo, label, i, 0, 0xff93c5dd, 0xffb8ffff, 0xffb8ffff, 0xff8c8c8c);
        }
        Address<AttachControl>(0xc9afa0)(ui.originalPane, combo);
        return combo;
    }

    static void Label(void* renderer, int x, int y, const wchar_t* title, DWORD ink = normalInk)
    {
        reinterpret_cast<DrawInlineText>(Field<void**>(renderer, 0)[0x5c / sizeof(void*)])(renderer, x, y, ink, title);
    }

    static const wchar_t* KeyCaption(int key, wchar_t (&name)[8])
    {
        switch (key)
        {
        case 0: return L"—";
        case VK_LEFT: return L"←";
        case VK_UP: return L"↑";
        case VK_RIGHT: return L"→";
        case VK_DOWN: return L"↓";
        case VK_SPACE: return L"Space";
        case VK_ESCAPE: return L"Esc";
        case VK_RETURN: return L"Enter";
        case VK_TAB: return L"Tab";
        case VK_HOME: return L"Home";
        case VK_END: return L"End";
        case VK_PRIOR: return L"PgUp";
        case VK_NEXT: return L"PgDn";
        }
        if (key >= VK_F1 && key <= VK_F12)
            swprintf_s(name, ARRAYSIZE(name), L"F%d", key - VK_F1 + 1);
        else
            swprintf_s(name, ARRAYSIZE(name), L"%lc", key);
        return name;
    }

    static void RefreshBinding(int index)
    {
        wchar_t name[8];
        bool action = index == bindingCount;
        Caption(action ? ui.repeatKey : ui.bindings[index], KeyCaption(action ? ui.values.repeatKey :
            ui.values.keys[index], name), index == ui.binding ? selectedInk : normalInk);
    }

    static void SelectBinding(int index)
    {
        const int previous = ui.binding;
        ui.binding = index;
        if (previous >= 0) RefreshBinding(previous);
        if (index >= 0) RefreshBinding(index);
    }

    static bool AssignBinding(UINT key)
    {
        if (ui.binding == bindingCount)
        {
            if (key == VK_ESCAPE) { SelectBinding(-1); return true; }
            if (!(key >= 'A' && key <= 'Z') && !(key >= '0' && key <= '9')) return false;
            ui.values.repeatKey = static_cast<int>(key);
            SelectBinding(-1);
            return true;
        }
        switch (key)
        {
        case VK_ESCAPE: case VK_RETURN: case VK_TAB: case VK_SPACE: case VK_HOME: case VK_END:
        case VK_LEFT: case VK_UP: case VK_RIGHT: case VK_DOWN: case VK_PRIOR: case VK_NEXT:
            break;
        case VK_DELETE: case VK_BACK: key = 0; break;
        default:
            if (!(key >= 'A' && key <= 'Z') && !(key >= '0' && key <= '9') &&
                !(key >= VK_F1 && key <= VK_F12)) return false;
        }
        ui.values.keys[ui.binding] = static_cast<int>(key);
        SelectBinding(-1);
        return true;
    }

    static bool WaitingForBinding()
    {
        return ui.active && ui.page != Page::Game && ui.binding >= 0 && GetForegroundWindow() == viewport;
    }

    static bool CaptureQueuedKey(const MSG& event)
    {
        if (event.message == WM_KILLFOCUS) { queuedBindingKey = 0; SuspendInput(); }
        const bool down = event.message == WM_KEYDOWN || event.message == WM_SYSKEYDOWN;
        const bool up = event.message == WM_KEYUP || event.message == WM_SYSKEYUP;
        // Chat and dialogs consume their own text and navigation messages. Disarm an
        // ongoing hold at the transition, even when the game draws its own edit box.
        if (down && (event.wParam == VK_RETURN || event.wParam == VK_ESCAPE)) SuspendInput();
        if (!down && !up) return false;
        const unsigned scan = (event.lParam >> 16) & 0x7f;
        const bool extended = (event.lParam & 0x01000000) != 0;
        const unsigned inputScan = scan ? scan | (extended ? 0x80 : 0) : 0;
        const bool sameKey = queuedBindingKey && (event.wParam == queuedBindingKey ||
            (inputScan && inputScan == queuedBindingScan));
        if (sameKey && (up || (event.lParam & 0x40000000)))
        {
            if (up) queuedBindingKey = 0;
            return true;
        }
        if (!down || !WaitingForBinding()) return false;
        UINT key = static_cast<UINT>(event.wParam);
        if (key == VK_PROCESSKEY)
        {
            if (readImeKey) key = readImeKey(event.hwnd);
            if ((key == VK_PROCESSKEY || !key) && scan)
                key = MapVirtualKeyW((extended ? 0xe000 : 0) | scan, MAPVK_VSC_TO_VK_EX);
        }
        if (!AssignBinding(key)) return false;
        queuedBindingKey = key;
        queuedBindingScan = capturedScan = inputScan;
        return true;
    }

    static HRESULT __fastcall PollSettingsKeyboard(void* keyboard, void*)
    {
        const HRESULT result = pollOriginal(keyboard);
        if (FAILED(result) || !Field<BYTE>(keyboard, 4)) { repeat.Suspend(); pace.Reset(); return result; }
        // Process the same DirectInput snapshot as the game's shortcut handlers.
        auto& keys = Field<BYTE[256]>(keyboard, 0x10d);
        auto& last = Field<BYTE[256]>(keyboard, 0x20d);
        const bool eligible = viewport && !ui.active && GetForegroundWindow() == viewport && GetFocus() == viewport;
        const unsigned town = eligible ? HandleToULong(GetPropW(viewport, L"AradHomeward.TownPace")) : 0;
        if (town == 25 || town == 50 || town == 75)
        {
            // Standard DirectInput cursor keys, shared by the keyboard and the default pad mapping.
            constexpr unsigned arrows[] = {0xcb, 0xcd, 0xc8, 0xd0};
            bool moving = false;
            for (auto scan : arrows) moving |= (keys[scan] & 0x80) != 0;
            if (!pace.Sample(town, moving, GetTickCount()))
                for (auto scan : arrows) keys[scan] = 0;
        }
        else pace.Reset();
        const unsigned setting = viewport ? HandleToULong(GetPropW(viewport, L"AradHomeward.HoldRepeat")) : 0;
        const unsigned key = setting & 255, rate = setting >> 8;
        if ((rate == 4 || rate == 8 || rate == 12) && ((key >= 'A' && key <= 'Z') || (key >= '0' && key <= '9')))
        {
            const unsigned scan = MapVirtualKeyW(key, MAPVK_VK_TO_VSC);
            if (scan && scan < ARRAYSIZE(keys))
            {
                const bool shaping = repeat.Shaping(), previous = repeat.WasDown();
                const bool pressed = repeat.Sample(setting, eligible, (keys[scan] & 0x80) != 0, GetTickCount());
                if (eligible && (repeat.Shaping() || shaping))
                {
                    keys[scan] = pressed ? 0x80 : 0;
                    if (shaping) last[scan] = previous ? 0x80 : 0;
                }
            }
        }
        else repeat.Suspend();
        if (capturedScan)
        {
            const bool held = (keys[capturedScan] & 0x80) != 0;
            keys[capturedScan] = last[capturedScan] = 0;
            if (!held) capturedScan = 0;
        }
        if (!WaitingForBinding()) return result;
        for (unsigned scan = 1; scan < ARRAYSIZE(keys); ++scan)
        {
            if (!(keys[scan] & 0x80) || (last[scan] & 0x80)) continue;
            const unsigned windowsScan = (scan & 0x80) ? 0xe000 | (scan & 0x7f) : scan;
            if (AssignBinding(MapVirtualKeyW(windowsScan, MAPVK_VSC_TO_VK_EX)))
            {
                capturedScan = scan;
                keys[scan] = 0;
                break;
            }
        }
        return result;
    }

    static void LayoutControls()
    {
        auto frame = Frame(ui.window);
        const int x = X(frame), y = Y(frame);
        const int footer = Y(Field<void*>(ui.window, 0x164));
        Move(ui.gameTab, x + 8, footer);
        Move(ui.padTab, x + 152, footer);
        Move(Field<void*>(ui.window, 0x168), x + 9, y + 331);
        Move(ui.size, x + 100, y + 365);
        // The combo moves up when opened. Anchor its list after the final field placement.
        Move(Field<void*>(ui.size, 0xac), X(ui.size), Y(ui.size) + 2);
        Move(ui.padPane, x, y);
        Move(ui.actionPane, x, y);
        Move(ui.townSpeed, x + 147, y + townRow);
        Move(ui.repeatSwitch, x + 147, y + repeatRow);
        Move(ui.repeatRate, x + 147, y + (ui.canToggleRepeat ? frequencyRow : repeatRow));
        Move(ui.repeatKey, x + 147, y + keyRow);
        Move(ui.openPad, x + 147, y + padRow);
        Move(ui.backToActions, x + 152, y + 363);
        Move(ui.enabled, x + 92, y + 30);
        Move(ui.defaults, x + 152, y + 30);
        for (int i = 0; i < bindingCount; ++i)
            Move(ui.bindings[i], x + (i % 2 ? 152 : 45), y + 61 + (i / 2) * 25);
    }

    static void* NewPane(void* window, void* frame, int x, int y)
    {
        auto storage = Address<Allocate>(0x401fb0)(0x70);
        if (!storage) return nullptr;
        auto pane = Address<BuildControl>(0x5704e0)(storage);
        Field<void*>(pane, 0x44) = window;
        Move(pane, x, y);
        Visible(pane, false);
        Address<AttachControl>(0xc9afa0)(frame, pane);
        return pane;
    }

    static bool CreateControls(void* window)
    {
        if (ui.window == window) return ui.ready;
        ui = {};
        ui.window = window;
        ui.originalPane = Field<void*>(window, 0xf4);
        ui.binding = -1;
        auto frame = Frame(window);
        const int x = X(frame), y = Y(frame);
        ui.padPane = NewPane(window, frame, x, y);
        ui.actionPane = NewPane(window, frame, x, y);
        if (!ui.padPane || !ui.actionPane) return false;
        ui.size = NewSizeCombo();
        Visible(ui.size, false);
        ui.gameTab = NewButton(frame, text->game, x + 8, y + 393);
        ui.padTab = NewButton(frame, text->pad, x + 152, y + 393);
        Visible(ui.gameTab, false);
        Visible(ui.padTab, false);
        if (!ui.size || !ui.gameTab || !ui.padTab) return false;
        ui.enabled = NewButton(ui.padPane, text->on, x + 92, y + 30);
        ui.defaults = NewButton(ui.padPane, text->defaults, x + 152, y + 30);
        ui.backToActions = NewButton(ui.padPane, Actions().actions, x + 152, y + 363);
        ui.townSpeed = NewButton(ui.actionPane, L"", x + 147, y + townRow);
        ui.repeatSwitch = NewButton(ui.actionPane, text->off, x + 147, y + repeatRow);
        ui.repeatRate = NewButton(ui.actionPane, L"", x + 147, y + frequencyRow);
        ui.repeatKey = NewButton(ui.actionPane, L"", x + 147, y + keyRow);
        ui.openPad = NewButton(ui.actionPane, text->pad, x + 147, y + padRow);
        if (!ui.enabled || !ui.defaults || !ui.backToActions || !ui.townSpeed || !ui.repeatSwitch || !ui.repeatRate ||
            !ui.repeatKey || !ui.openPad) return false;
        for (int i = 0; i < bindingCount; ++i)
        {
            ui.bindings[i] = NewButton(ui.padPane, L"", x + (i % 2 ? 152 : 45), y + 61 + (i / 2) * 25);
            if (!ui.bindings[i]) return false;
        }
        ui.ready = true;
        return true;
    }

    static void RefreshActions()
    {
        wchar_t value[16];
        swprintf_s(value, L"%d%%", ui.values.townSpeed);
        Caption(ui.townSpeed, value);
        if (ui.canToggleRepeat)
            Caption(ui.repeatSwitch, ui.values.repeatEnabled ? text->on : text->off,
                ui.values.repeatEnabled ? selectedInk : normalInk);
        swprintf_s(value, L"%d / s", ui.values.repeatRate);
        Caption(ui.repeatRate, ui.values.repeatRate ? value : text->off);
        RefreshBinding(bindingCount);
    }

    template<size_t count> static int Next(int current, const int (&choices)[count])
    {
        for (size_t i = 0; i < count; ++i)
            if (choices[i] == current) return choices[(i + 1) % count];
        return choices[0];
    }

    static void ShowPage(Page page)
    {
        ui.page = page;
        SelectBinding(-1);
        Visible(ui.originalPane, page == Page::Game);
        Visible(ui.padPane, page == Page::Pad);
        Visible(ui.actionPane, page == Page::Actions);
        Visible(ui.repeatSwitch, page == Page::Actions && ui.canToggleRepeat);
        Visible(ui.backToActions, page == Page::Pad && ui.extended);
        Caption(ui.gameTab, text->game, page == Page::Game ? selectedInk : normalInk);
        Caption(ui.padTab, ui.extended ? Actions().controls : text->pad, page == Page::Game ? normalInk : selectedInk);
    }

    static void HideControls()
    {
        if (!ui.active) return;
        ui.active = false;
        Visible(ui.padPane, false);
        Visible(ui.actionPane, false);
        Visible(ui.gameTab, false);
        Visible(ui.padTab, false);
        Visible(ui.size, false);
        if (ui.page != Page::Game && Field<int>(ui.window, 0x1ac) == gameSettingsTab) Visible(ui.originalPane, true);
        auto frame = Frame(ui.window);
        Move(Field<void*>(ui.window, 0x168), X(frame) + 9, Y(frame) + 365);
    }

    static void SelectMode(int mode)
    {
        ui.values.display = mode;
        Address<SelectEntry>(0xc86950)(Field<void*>(ui.window, 0x124), mode);
    }

    static bool __fastcall OpenSettings(void* window, void*, int context, int tab)
    {
        const bool opened = openOriginal(window, context, tab);
        if (!opened) return false;
        if (tab != gameSettingsTab)
        {
            HideControls();
            return true;
        }
        if (!CreateControls(window) || !ReadPreferences())
        {
            HideControls();
            return true;
        }
        ui.active = true;
        SuspendInput();
        ui.saveError = false;
        LayoutControls();
        SelectMode(ui.values.display);
        int index = 0;
        for (int i = 0; i < ARRAYSIZE(scales); ++i)
            if (ui.values.scale == scales[i]) index = i;
        Address<SelectEntry>(0xc5ec30)(ui.size, index);
        Visible(ui.size, true);
        Visible(ui.gameTab, true);
        Visible(ui.padTab, true);
        ShowPage(Page::Game);
        if (ui.extended) RefreshActions();
        Caption(ui.enabled, ui.values.padEnabled ? text->on : text->off);
        for (int i = 0; i < bindingCount; ++i) RefreshBinding(i);
        return true;
    }

    static void __fastcall DrawSettings(void* window, void*, void* control)
    {
        drawOriginal(window, control);
        if (!ui.active || window != ui.window) return;
        if (control != (ui.page == Page::Game ? ui.originalPane : ui.page == Page::Pad ? ui.padPane : ui.actionPane)) return;
        auto renderer = *Address<void**>(0x129647c);
        if (!renderer) return;
        // The native renderer keeps a pointer to the active style until it is popped.
        const FontStyle style = {0, 0, 190, 1, 0, 0, *Address<void**>(0x11abbc4)};
        auto methods = Field<void**>(renderer, 0);
        reinterpret_cast<SetFontStyle>(methods[0x4c / sizeof(void*)])(renderer, &style);
        const int x = X(Frame(window)), y = Y(Frame(window));
        if (ui.page == Page::Game)
        {
            Label(renderer, x + 9, y + 368, text->size);
            if (ui.saveError) Label(renderer, x + 9, y + 347, text->saveError, 0xffffa592);
        }
        else if (ui.page == Page::Pad)
        {
            Label(renderer, x + 9, y + 35, text->enabled);
            for (int i = 0; i < bindingCount; ++i)
                Label(renderer, x + (i % 2 ? 106 : 3), y + 66 + (i / 2) * 25, padActions[i]);
            Label(renderer, x + 9, y + 345, ui.saveError ? text->saveError : ui.binding >= 0 ? text->pressKey : text->hint,
                ui.saveError ? 0xffffa592 : selectedInk);
        }
        else
        {
            Label(renderer, x + 9, y + townRow + 5, Actions().town);
            Label(renderer, x + 9, y + repeatRow + 5, Actions().repeat);
            if (ui.canToggleRepeat) Label(renderer, x + 9, y + frequencyRow + 5, Actions().frequency);
            Label(renderer, x + 9, y + keyRow + 5, Actions().key);
            if (ui.binding == bindingCount) Label(renderer, x + 9, y + keyRow + 32, Actions().keyHint, selectedInk);
            if (ui.saveError) Label(renderer, x + 9, y + 345, text->saveError, 0xffffa592);
        }
        reinterpret_cast<RestoreFontStyle>(methods[0x50 / sizeof(void*)])(renderer);
    }

    static bool Clicked(void* control)
    {
        return Field<BYTE>(control, 0x11) && Field<int>(control, 0x0c) == *Address<int*>(0x1199cc8);
    }

    static void __fastcall UpdateSettings(void* window, void*)
    {
        if (ui.active && window == ui.window)
        {
            if (Clicked(ui.gameTab)) ShowPage(Page::Game);
            else if (Clicked(ui.padTab)) ShowPage(ui.extended ? Page::Actions : Page::Pad);
            else if (ui.page == Page::Actions)
            {
                if (Clicked(ui.townSpeed)) { ui.values.townSpeed = Next(ui.values.townSpeed, townSpeeds); RefreshActions(); }
                else if (ui.canToggleRepeat && Clicked(ui.repeatSwitch))
                {
                    ui.values.repeatEnabled = !ui.values.repeatEnabled;
                    SelectBinding(-1);
                    RefreshActions();
                }
                else if (Clicked(ui.repeatRate))
                {
                    ui.values.repeatRate = ui.canToggleRepeat ? Next(ui.values.repeatRate, repeatRates)
                        : Next(ui.values.repeatRate, legacyRepeatRates);
                    RefreshActions();
                }
                else if (Clicked(ui.repeatKey)) SelectBinding(bindingCount);
                else if (Clicked(ui.openPad)) ShowPage(Page::Pad);
            }
            else if (ui.page == Page::Pad)
            {
                if (ui.extended && Clicked(ui.backToActions)) ShowPage(Page::Actions);
                else if (Clicked(ui.enabled))
                {
                    ui.values.padEnabled = !ui.values.padEnabled;
                    Caption(ui.enabled, ui.values.padEnabled ? text->on : text->off);
                    SelectBinding(-1);
                }
                else if (Clicked(ui.defaults))
                {
                    const HWND endpoint = Endpoint();
                    for (int i = 0; i < bindingCount; ++i)
                    {
                        const int key = ReadPreference(endpoint, defaultBinding + i);
                        if (key >= 0) ui.values.keys[i] = key;
                        RefreshBinding(i);
                    }
                    SelectBinding(-1);
                }
                else
                {
                    for (int i = 0; i < bindingCount; ++i)
                        if (Clicked(ui.bindings[i])) { SelectBinding(i); break; }
                }
            }
        }
        updateOriginal(window);
        if (ui.active && window == ui.window) SelectMode(ui.values.display);
    }

    static void __fastcall FinishSettings(void* window, void*)
    {
        if (ui.active && window == ui.window && _ReturnAddress() == Address<void*>(0xaad1b7) && !SavePreferences())
        {
            ui.saveError = true;
            return;
        }
        finishOriginal(window);
    }

    static void __fastcall CloseSettings(void* window, void*)
    {
        if (window == ui.window) HideControls();
        closeOriginal(window);
    }

    static void* __fastcall DestroySettings(void* window, void*, unsigned flags)
    {
        if (window == ui.window) ui = {};
        return destroyOriginal(window, flags);
    }

    static void __cdecl PrepareNativeDisplay(int mode)
    {
        if (!ui.active) Address<NativeUpdateDisplay>(0xba8f10)(mode);
    }

    static bool __fastcall ChangeScreenMode(void* config, void*, int option, int mode)
    {
        if (ui.active && mode >= windowed && mode <= fullScreen)
        {
            SelectMode(mode);
            return true;
        }
        const bool changed = Address<WriteOption>(0xbe4f30)(config, option, mode);
        PublishScreenMode(config);
        return changed;
    }

    static bool IsCall(const BYTE* site, const void* target)
    {
        int displacement = 0;
        memcpy(&displacement, site + 1, sizeof(displacement));
        return *site == 0xe8 && site + 5 + displacement == target;
    }

    static void ReplaceCall(BYTE* site, const void* target)
    {
        const auto displacement = reinterpret_cast<INT_PTR>(target) - reinterpret_cast<INT_PTR>(site + 5);
        memcpy(site + 1, &displacement, sizeof(displacement));
    }

    static bool Install(BYTE* client, const wchar_t* locale)
    {
        image = client;
        auto table = Address<void**>(0xf985bc);
        auto poll = Address<void**>(0xfc6d28);
        auto prepare = Address<BYTE*>(0xaaaffa);
        auto change = Address<BYTE*>(0xaab01e);
        if (table[0] != Address<void*>(0xa1c4a0) || table[1] != Address<void*>(0xaacac0) ||
            table[2] != Address<void*>(0xaad090) || table[4] != Address<void*>(0xaaf0c0) ||
            table[5] != Address<void*>(0xaab6e0) || table[20] != Address<void*>(0xa16250) ||
            *poll != Address<void*>(0xc81190) ||
            !IsCall(prepare, Address<void*>(0xba8f10)) || !IsCall(change, Address<void*>(0xbe4f30)))
            return false;
        readMessage = RegisterWindowMessageW(L"AradHomeward.GamePreferences.v1");
        if (!readMessage) return false;
        if (HMODULE imm = GetModuleHandleW(L"imm32.dll"))
            readImeKey = reinterpret_cast<ReadImeKey>(GetProcAddress(imm, "ImmGetVirtualKey"));
        if (wcscmp(locale, L"zh-Hant") == 0) text = &localized[1];
        else if (wcscmp(locale, L"ja-JP") == 0) text = &localized[3];
        else if (wcscmp(locale, L"ko-KR") == 0) text = &localized[4];
        else if (wcscmp(locale, L"zh-Hans") != 0) text = &localized[2];
        DWORD protection, codeProtection, pollProtection, ignored;
        const size_t codeBytes = change + 5 - prepare;
        if (!VirtualProtect(table, 21 * sizeof(void*), PAGE_READWRITE, &protection)) return false;
        if (!VirtualProtect(prepare, codeBytes, PAGE_EXECUTE_READWRITE, &codeProtection))
        {
            VirtualProtect(table, 21 * sizeof(void*), protection, &ignored);
            return false;
        }
        if (!VirtualProtect(poll, sizeof(void*), PAGE_READWRITE, &pollProtection))
        {
            VirtualProtect(prepare, codeBytes, codeProtection, &ignored);
            VirtualProtect(table, 21 * sizeof(void*), protection, &ignored);
            return false;
        }
        destroyOriginal = reinterpret_cast<DestroyWindow>(table[0]);
        drawOriginal = reinterpret_cast<DrawWindow>(table[1]);
        updateOriginal = reinterpret_cast<UpdateWindow>(table[2]);
        openOriginal = reinterpret_cast<OpenWindow>(table[4]);
        closeOriginal = reinterpret_cast<UpdateWindow>(table[5]);
        finishOriginal = reinterpret_cast<UpdateWindow>(table[20]);
        pollOriginal = reinterpret_cast<PollKeyboard>(*poll);
        table[0] = reinterpret_cast<void*>(&DestroySettings);
        table[1] = reinterpret_cast<void*>(&DrawSettings);
        table[2] = reinterpret_cast<void*>(&UpdateSettings);
        table[4] = reinterpret_cast<void*>(&OpenSettings);
        table[5] = reinterpret_cast<void*>(&CloseSettings);
        table[20] = reinterpret_cast<void*>(&FinishSettings);
        *poll = reinterpret_cast<void*>(&PollSettingsKeyboard);
        ReplaceCall(prepare, reinterpret_cast<void*>(&PrepareNativeDisplay));
        ReplaceCall(change, reinterpret_cast<void*>(&ChangeScreenMode));
        VirtualProtect(prepare, codeBytes, codeProtection, &ignored);
        VirtualProtect(poll, sizeof(void*), pollProtection, &ignored);
        VirtualProtect(table, 21 * sizeof(void*), protection, &ignored);
        FlushInstructionCache(GetCurrentProcess(), prepare, codeBytes);
        return true;
    }
}
