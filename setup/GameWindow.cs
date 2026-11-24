// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

internal static class GameWindow
{
    internal const int OriginalWidth = 640, OriginalHeight = 480;
    private const string InputReady = "AradHomeward.WindowInputReady";
    private const string ScaleWidth = "AradHomeward.WindowScaleWidth";
    private const uint DoNotActivate = 0x0010, DoNotChangeZOrder = 0x0004, DoNotResize = 0x0001;
    private const uint Async = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal int Width { get { return Right - Left; } }
        internal int Height { get { return Bottom - Top; } }
        internal Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static Size WindowSize(Size available, int percent, int chatWidth)
    {
        int maximum = Math.Min((available.Width - chatWidth) / 4, available.Height / 3);
        int original = OriginalWidth / 4;
        int preferred = percent == 0 ? maximum : original * percent / 100;
        int unit = Math.Max(original, Math.Min(maximum, preferred));
        return new Size(unit * 4, unit * 3);
    }

    internal static void Monitor(Process game, int percent, Action<string> log)
    {
        IntPtr observed = IntPtr.Zero;
        var age = new Stopwatch();
        Size lastReported = Size.Empty;
        bool missingReported = false;
        while (!game.WaitForExit(300))
        {
            game.Refresh();
            IntPtr window = game.MainWindowHandle;
            if (window != observed)
            {
                observed = window;
                age.Restart();
            }
            if (window == IntPtr.Zero || IsIconic(window) || age.ElapsedMilliseconds < 850)
                continue;
            if (GetProp(window, InputReady) == IntPtr.Zero)
            {
                // Let the client's dispatcher attach on its own UI thread,
                // even while the newly opened game does not have focus yet.
                PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
                if (!missingReported && age.ElapsedMilliseconds > 15000)
                {
                    log("DISPLAY client window does not provide scaled mouse input");
                    missingReported = true;
                }
                continue;
            }

            Size applied;
            if (TryApply(window, (uint)game.Id, percent, out applied) && applied != lastReported)
            {
                log(String.Format("DISPLAY pid={0} window={1}x{2} requested={3}", game.Id,
                    applied.Width, applied.Height, percent == 0 ? "fit" : percent + "%"));
                lastReported = applied;
            }
        }
    }

    private static bool TryApply(IntPtr window, uint pid, int percent, out Size applied)
    {
        applied = Size.Empty;
        IntPtr oldContext = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
        try
        {
            Rect client, frame;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetClientRect(window, out client) || !GetWindowRect(window, out frame) ||
                !GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor) ||
                client.Width < OriginalWidth || client.Height < OriginalHeight)
                return false;

            // Preserve the client's own full-screen mode, including transitions
            // back to a regular window.
            if (frame.Bounds == monitor.Monitor.Bounds && GetProp(window, ScaleWidth) == IntPtr.Zero)
                return false;

            IntPtr chat = FindChat(pid);
            Rect chatFrame = new Rect();
            bool visibleChat = chat != IntPtr.Zero && IsWindowVisible(chat) && GetWindowRect(chat, out chatFrame);
            int chatWidth = visibleChat ? chatFrame.Width : 0;
            int borderWidth = Math.Max(0, frame.Width - client.Width);
            int borderHeight = Math.Max(0, frame.Height - client.Height);
            Size available = new Size(monitor.Work.Width - borderWidth, monitor.Work.Height - borderHeight);
            Size desired = WindowSize(available, percent, chatWidth);
            int selectedWidth = desired.Width > OriginalWidth ? desired.Width : 0;
            if (GetProp(window, ScaleWidth) != new IntPtr(selectedWidth))
            {
                if (selectedWidth == 0) RemoveProp(window, ScaleWidth);
                else if (!SetProp(window, ScaleWidth, new IntPtr(selectedWidth))) return false;
            }

            const uint flags = DoNotActivate | DoNotChangeZOrder | Async;
            if (client.Width != desired.Width || client.Height != desired.Height)
            {
                int width = desired.Width + borderWidth, height = desired.Height + borderHeight;
                int x = Clamp(frame.Left + (frame.Width - width) / 2,
                    monitor.Work.Left, monitor.Work.Right - width - chatWidth);
                int y = Clamp(frame.Top + (frame.Height - height) / 2,
                    monitor.Work.Top, monitor.Work.Bottom - height);
                SetWindowPos(window, IntPtr.Zero, x, y, width, height, flags);
                return false; // Verify the applied client size on the next observation.
            }

            if (visibleChat && (chatFrame.Left != frame.Right || chatFrame.Top != frame.Top))
                SetWindowPos(chat, IntPtr.Zero, frame.Right, frame.Top, 0, 0, flags | DoNotResize);
            applied = desired;
            return true;
        }
        finally
        {
            if (oldContext != IntPtr.Zero) SetThreadDpiAwarenessContext(oldContext);
        }
    }

    private static IntPtr FindChat(uint pid)
    {
        IntPtr window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, "DNF_WND_CHAT", null)) != IntPtr.Zero)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == pid) return window;
        }
        return IntPtr.Zero;
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(value, maximum));
    }
}
