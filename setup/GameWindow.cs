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
    private const string ScaleSize = "AradHomeward.WindowScaleSize";
    private const string ChatNativeWidth = "AradHomeward.ChatNativeWidth";
    private const string ChatScaleSize = "AradHomeward.ChatScaleSize";
    private const string NativeScreenMode = "AradHomeward.NativeScreenMode";
    private const uint DoNotActivate = 0x0010, DoNotChangeZOrder = 0x0004, DoNotResize = 0x0001;
    private const uint Async = 0x4000;
    private static volatile bool dpiContextsUnavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal int Width { get { return Right - Left; } }
        internal int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
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

    internal static IntPtr TrySetDpiContext(IntPtr context)
    {
        if (!dpiContextsUnavailable && context != IntPtr.Zero)
        {
            try { return SetThreadDpiAwarenessContext(context); }
            catch (EntryPointNotFoundException) { dpiContextsUnavailable = true; }
        }
        return IntPtr.Zero;
    }

    private static IntPtr MatchWindowDpiContext(IntPtr window)
    {
        // Older Windows versions use the process DPI awareness from the manifest.
        if (!dpiContextsUnavailable)
        {
            try { return TrySetDpiContext(GetWindowDpiAwarenessContext(window)); }
            catch (EntryPointNotFoundException) { dpiContextsUnavailable = true; }
        }
        return IntPtr.Zero;
    }

    internal static Size WindowSize(Size available, int percent, int nativeChatWidth)
    {
        int original = OriginalWidth / 4;
        int maximum = Math.Min(available.Width * original / (OriginalWidth + nativeChatWidth), available.Height / 3);
        int preferred = percent == 0 ? maximum : original * percent / 100;
        int unit = Math.Max(original, Math.Min(maximum, preferred));
        return new Size(unit * 4, unit * 3);
    }

    internal static void Monitor(Process game, Func<GamePlaySettings> getSettings, Action<string> log)
    {
        IntPtr observed = IntPtr.Zero;
        var age = new Stopwatch();
        Size lastReported = Size.Empty, lastReportedChat = Size.Empty;
        bool missingReported = false, managingLayout = false;
        while (!game.WaitForExit(300))
        {
            game.Refresh();
            IntPtr window = game.MainWindowHandle;
            if (window != observed)
            {
                observed = window;
                age.Restart();
                lastReported = Size.Empty;
                lastReportedChat = Size.Empty;
            }
            if (window == IntPtr.Zero || IsIconic(window) || age.ElapsedMilliseconds < 850)
                continue;
            var settings = getSettings();
            bool fullscreen = settings.DisplayMode == GameDisplayMode.FullScreen;
            int percent = settings.DisplayMode == GameDisplayMode.Windowed ? settings.WindowScale : 0;
            if (percent != 100 || fullscreen) managingLayout = true;
            if (!managingLayout) continue;
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

            Size applied, appliedChat;
            if (!TryApply(window, (uint)game.Id, percent, fullscreen, out applied, out appliedChat)) continue;
            managingLayout = percent != 100 || fullscreen;
            if (applied != lastReported || appliedChat != lastReportedChat)
            {
                log(String.Format("DISPLAY pid={0} window={1}x{2} requested={3} chat={4}", game.Id,
                    applied.Width, applied.Height, fullscreen ? "fullscreen" : percent == 0 ? "fit" : percent + "%",
                    appliedChat.IsEmpty ? "hidden" : String.Format("{0}x{1}", appliedChat.Width, appliedChat.Height)));
                lastReported = applied;
                lastReportedChat = appliedChat;
            }
        }
    }

    private static bool TryApply(IntPtr window, uint pid, int percent, bool fullscreen, out Size applied, out Size appliedChat)
    {
        applied = appliedChat = Size.Empty;
        if (GetProp(window, NativeScreenMode).ToInt64() > 1 || IsZoomed(window)) return false;
        IntPtr oldContext = MatchWindowDpiContext(window);
        try
        {
            Rect client, frame;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetClientRect(window, out client) || !GetWindowRect(window, out frame) ||
                !GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor) ||
                client.Width <= 0 || client.Height <= 0)
                return false;

            IntPtr chat = FindChat(pid);
            Rect chatFrame = new Rect(), chatClient = new Rect();
            bool hasChat = chat != IntPtr.Zero && GetWindowRect(chat, out chatFrame) && GetClientRect(chat, out chatClient);
            bool visibleChat = hasChat && IsWindowVisible(chat);
            int nativeChatWidth = hasChat ? GetProp(chat, ChatNativeWidth).ToInt32() : 0;
            if (visibleChat && nativeChatWidth == 0)
                PostMessage(chat, 0, IntPtr.Zero, IntPtr.Zero);

            int borderWidth = Math.Max(0, frame.Width - client.Width);
            int borderHeight = Math.Max(0, frame.Height - client.Height);
            int chatBorderWidth = Math.Max(0, chatFrame.Width - chatClient.Width);
            int chatBorderHeight = Math.Max(0, chatFrame.Height - chatClient.Height);
            int reservedWidth = !visibleChat ? 0 : nativeChatWidth == 0 ? chatFrame.Width : chatBorderWidth;
            int reservedHeight = visibleChat && nativeChatWidth != 0 ? Math.Max(borderHeight, chatBorderHeight) : borderHeight;
            Rect area = fullscreen ? monitor.Monitor : monitor.Work;
            var available = new Size(area.Width - borderWidth - reservedWidth, area.Height - reservedHeight);
            int visibleNativeChatWidth = visibleChat ? nativeChatWidth : 0;
            Size desired = fullscreen ? new Size(available.Width * OriginalWidth / (OriginalWidth + visibleNativeChatWidth),
                available.Height) : WindowSize(available, percent, visibleNativeChatWidth);
            if (desired.Width <= 0 || desired.Height <= 0) return false;
            int viewportSize = fullscreen ? (desired.Height << 16) | desired.Width : 0;
            if (!SetScale(window, ScaleSize, viewportSize) ||
                !SetScale(window, ScaleWidth, !fullscreen && desired.Width > OriginalWidth ? desired.Width : 0)) return false;

            const uint flags = DoNotActivate | DoNotChangeZOrder | Async;
            bool resizeGame = client.Width != desired.Width || client.Height != desired.Height;
            int chatWidth = chatFrame.Width, chatHeight = chatFrame.Height;
            bool resizeChat = false;
            if (nativeChatWidth != 0)
            {
                int scaled = fullscreen && visibleChat ? available.Width - desired.Width :
                    (nativeChatWidth * desired.Height + OriginalHeight / 2) / OriginalHeight;
                int scale = desired.Height == OriginalHeight && scaled == nativeChatWidth ? 0 : (desired.Height << 16) | scaled;
                if (!SetScale(chat, ChatScaleSize, scale)) return false;
                chatWidth = scaled + chatBorderWidth;
                chatHeight = desired.Height + chatBorderHeight;
                resizeChat = chatClient.Width != scaled || chatClient.Height != desired.Height;
            }

            int width = desired.Width + borderWidth, height = desired.Height + borderHeight;
            int x = frame.Left, y = frame.Top;
            if (fullscreen)
            {
                x = area.Left;
                y = area.Top;
            }
            else if (resizeGame || (visibleChat && resizeChat))
            {
                x = Clamp(x + (frame.Width - width) / 2,
                    area.Left, area.Right - width - (visibleChat ? chatWidth : 0));
                y = Clamp(y + (frame.Height - height) / 2,
                    area.Top, area.Bottom - (visibleChat ? Math.Max(height, chatHeight) : height));
            }
            if (resizeGame || x != frame.Left || y != frame.Top)
                SetWindowPos(window, IntPtr.Zero, x, y, width, height, flags);

            // A hidden chat can be restored to the native size before it is
            // shown again. Leave its visibility under the client's control.
            bool moveChat = visibleChat && (chatFrame.Left != x + width || chatFrame.Top != y);
            if (resizeChat || moveChat)
                SetWindowPos(chat, IntPtr.Zero, x + width, y, chatWidth, chatHeight, flags | (resizeChat ? 0 : DoNotResize));
            if (resizeGame || resizeChat || moveChat) return false;

            applied = desired;
            if (visibleChat) appliedChat = new Size(chatClient.Width, chatClient.Height);
            return true;
        }
        finally
        {
            if (oldContext != IntPtr.Zero) SetThreadDpiAwarenessContext(oldContext);
        }
    }

    private static bool SetScale(IntPtr window, string property, int size)
    {
        var value = new IntPtr(size);
        if (GetProp(window, property) == value) return true;
        if (size != 0) return SetProp(window, property, value);
        return RemoveProp(window, property) != IntPtr.Zero;
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
