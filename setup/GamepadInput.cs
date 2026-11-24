// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[StructLayout(LayoutKind.Sequential)]
internal struct XboxGamepad
{
    internal ushort Buttons;
    internal byte LeftTrigger, RightTrigger;
    internal short LeftX, LeftY, RightX, RightY;
}

internal sealed class GamepadBindings
{
    private const ushort Up = 1, Down = 2, Left = 4, Right = 8;
    private const ushort Start = 0x10, Back = 0x20, LeftShoulder = 0x100, RightShoulder = 0x200;
    private static readonly ushort[] FaceButtons = { 0x4000, 0x1000, 0x8000, 0x2000 };
    private const ushort MappedButtons = Up | Down | Left | Right | Start | Back | LeftShoulder | RightShoulder | 0xf000;
    private const int PressThreshold = 12000, ReleaseThreshold = 9000;
    private readonly Keys[] keys;
    private readonly Keys[] faceKeys = new Keys[4];
    private ushort previousButtons;
    private int horizontal, vertical;
    private bool ready;

    internal GamepadBindings(Keys[] bindings) { keys = bindings; }

    internal void Reset()
    {
        previousButtons = 0;
        horizontal = vertical = 0;
        ready = false;
    }

    private static int DigitalAxis(ushort buttons, ushort negative, ushort positive)
    {
        return ((buttons & positive) != 0 ? 1 : 0) - ((buttons & negative) != 0 ? 1 : 0);
    }

    private static int StickAxis(int value, int previous)
    {
        if (value >= (previous > 0 ? ReleaseThreshold : PressThreshold)) return 1;
        if (value <= -(previous < 0 ? ReleaseThreshold : PressThreshold)) return -1;
        return 0;
    }

    internal void Collect(XboxGamepad state, HashSet<Keys> desired)
    {
        ushort buttons = state.Buttons;
        if (!ready)
        {
            ready = (buttons & MappedButtons) == 0 &&
                Math.Abs((int)state.LeftX) < ReleaseThreshold && Math.Abs((int)state.LeftY) < ReleaseThreshold;
            if (!ready) return;
        }

        horizontal = StickAxis(state.LeftX, horizontal);
        vertical = StickAxis(state.LeftY, vertical);
        int x = (buttons & (Left | Right)) != 0 ? DigitalAxis(buttons, Left, Right) : horizontal;
        int y = (buttons & (Down | Up)) != 0 ? DigitalAxis(buttons, Down, Up) : vertical;
        if (x != 0) desired.Add(keys[x < 0 ? 0 : 1]);
        if (y != 0) desired.Add(keys[y > 0 ? 2 : 3]);

        int layer = ((buttons & LeftShoulder) != 0 ? 1 : 0) + ((buttons & RightShoulder) != 0 ? 2 : 0);
        for (int i = 0; i < FaceButtons.Length; i++)
        {
            ushort face = FaceButtons[i];
            if ((buttons & face) == 0) continue;
            if ((previousButtons & face) == 0) faceKeys[i] = keys[4 + layer * 4 + i];
            desired.Add(faceKeys[i]);
        }
        if ((buttons & Start) != 0) desired.Add(keys[20]);
        if ((buttons & Back) != 0) desired.Add(keys[21]);
        desired.Remove(Keys.None);
        previousButtons = buttons;
    }
}

internal static class GamepadInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { internal uint Packet; internal XboxGamepad Pad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey, ScanCode;
        internal uint Flags, Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X, Y;
        internal uint Data, Flags, Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputFields
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { internal uint Type; internal InputFields Fields; }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint controller, out XInputState state);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyW(uint key, uint mapType);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint SendInput(uint count, ref Input input, int size);
    private static readonly int InputSize = Marshal.SizeOf(typeof(Input));

    internal static void Monitor(Process game, GamePlaySettings settings, Action<string> log)
    {
        var mapper = new GamepadBindings(settings.Bindings);
        var held = new HashSet<Keys>();
        var desired = new HashSet<Keys>();
        var releases = new List<Keys>();
        int controller = -1;
        bool connectedLogged = false, failureLogged = false;
        try
        {
            while (!game.HasExited)
            {
                uint foreground;
                GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                desired.Clear();
                int interval = 16;
                if (foreground != (uint)game.Id)
                {
                    mapper.Reset();
                    interval = 48;
                }
                else
                {
                    XInputState state;
                    int previous = controller;
                    if (ReadController(ref controller, out state))
                    {
                        if (previous != controller)
                        {
                            mapper.Reset();
                            connectedLogged = false;
                        }
                        if (!connectedLogged)
                        {
                            log("INPUT XInput controller connected slot=" + controller);
                            connectedLogged = true;
                        }
                        mapper.Collect(state.Pad, desired);
                    }
                    else
                    {
                        mapper.Reset();
                        if (connectedLogged) log("INPUT XInput controller disconnected");
                        connectedLogged = false;
                        interval = 250;
                    }
                }

                if (!ApplyKeys(held, desired, releases) && !failureLogged)
                {
                    log("INPUT Windows did not accept a controller keystroke");
                    failureLogged = true;
                }
                if (game.WaitForExit(interval)) break;
            }
        }
        finally
        {
            desired.Clear();
            ApplyKeys(held, desired, releases);
        }
    }

    private static bool ReadController(ref int controller, out XInputState state)
    {
        if (controller >= 0)
        {
            if (XInputGetState((uint)controller, out state) == 0) return true;
            // Release any keys from this controller before selecting a different slot.
            controller = -1;
            return false;
        }
        for (int slot = 0; slot < 4; slot++)
        {
            if (XInputGetState((uint)slot, out state) != 0) continue;
            controller = slot;
            return true;
        }
        state = new XInputState();
        return false;
    }

    private static bool ApplyKeys(HashSet<Keys> held, HashSet<Keys> desired, List<Keys> releases)
    {
        bool sent = true;
        releases.Clear();
        foreach (Keys key in held)
            if (!desired.Contains(key)) releases.Add(key);
        foreach (Keys key in releases)
        {
            if (SendKey(key, true)) held.Remove(key);
            else sent = false;
        }
        foreach (Keys key in desired)
        {
            if (held.Contains(key)) continue;
            if (SendKey(key, false)) held.Add(key);
            else sent = false;
        }
        return sent;
    }

    private static bool SendKey(Keys key, bool release)
    {
        uint scan = MapVirtualKeyW((uint)key, 4); // MAPVK_VK_TO_VSC_EX
        if (scan == 0) return false;
        var input = new Input
        {
            Type = 1, // INPUT_KEYBOARD
            Fields = new InputFields
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = (ushort)(scan & 0xff),
                    Flags = 8u | (scan >> 8 == 0xe0 ? 1u : 0u) | (release ? 2u : 0u)
                }
            }
        };
        return SendInput(1, ref input, InputSize) == 1;
    }
}
