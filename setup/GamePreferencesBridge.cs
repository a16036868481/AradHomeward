// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class GamePreferencesBridge : ApplicationContext
{
    private const string EndpointProperty = "AradHomeward.GamePreferences";
    private const int CopyDataMessage = 0x004a, Packet = 0x41524832, FirstBinding = 3, DefaultBinding = 0x100;
    private static readonly int ReadMessage = RegisterWindowMessage("AradHomeward.GamePreferences.v1");
    private readonly Process game;
    private readonly string root;
    private readonly Action<GamePlaySettings> apply;
    private readonly Timer timer = new Timer { Interval = 100 };
    private readonly Endpoint endpoint;
    private readonly GamePlaySettings defaults = new GamePlaySettings();
    private GamePlaySettings current;
    private IntPtr viewport;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        internal IntPtr Tag;
        internal int Length;
        internal IntPtr Data;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr window, string name);

    internal GamePreferencesBridge(Process game, string root, GamePlaySettings initial, Action<GamePlaySettings> apply)
    {
        this.game = game;
        this.root = root;
        current = initial;
        this.apply = apply;
        endpoint = new Endpoint(this);
        timer.Tick += (sender, args) => Connect();
        Connect();
        timer.Start();
    }

    private void Connect()
    {
        if (game.HasExited) { ExitThread(); return; }
        game.Refresh();
        IntPtr window = game.MainWindowHandle;
        if (window == viewport) return;
        Disconnect();
        if (ReadMessage != 0 && window != IntPtr.Zero && SetProp(window, EndpointProperty, endpoint.Handle))
            viewport = window;
    }

    private void Disconnect()
    {
        if (viewport != IntPtr.Zero && GetProp(viewport, EndpointProperty) == endpoint.Handle)
            RemoveProp(viewport, EndpointProperty);
        viewport = IntPtr.Zero;
    }

    private int Read(int field)
    {
        if (field == 0) return (int)current.DisplayMode + 1;
        if (field == 1) return current.WindowScale + 1;
        if (field == 2) return current.GamepadEnabled ? 2 : 1;
        if (field >= FirstBinding && field < FirstBinding + current.Bindings.Length)
            return (int)current.Bindings[field - FirstBinding] + 1;
        if (field >= DefaultBinding && field < DefaultBinding + defaults.Bindings.Length)
            return (int)defaults.Bindings[field - DefaultBinding] + 1;
        return 0;
    }

    private bool Save(IntPtr message)
    {
        if (message == IntPtr.Zero) return false;
        var data = (CopyData)Marshal.PtrToStructure(message, typeof(CopyData));
        var next = new GamePlaySettings();
        int length = FirstBinding + next.Bindings.Length;
        if (data.Tag != new IntPtr(Packet) || data.Length != length * sizeof(int) || data.Data == IntPtr.Zero)
            return false;
        var values = new int[length];
        Marshal.Copy(data.Data, values, 0, length);
        if (!Enum.IsDefined(typeof(GameDisplayMode), values[0]) || !GamePlaySettings.WindowScales.Contains(values[1]) ||
            values[2] < 0 || values[2] > 1) return false;
        next.DisplayMode = (GameDisplayMode)values[0];
        next.WindowScale = values[1];
        next.GamepadEnabled = values[2] != 0;
        for (int i = 0; i < next.Bindings.Length; i++)
        {
            var key = (Keys)values[FirstBinding + i];
            if (!GamePlaySettings.BindingChoices.Contains(key)) return false;
            next.Bindings[i] = key;
        }
        if (current.DisplayMode == next.DisplayMode && current.WindowScale == next.WindowScale &&
            current.GamepadEnabled == next.GamepadEnabled && current.Bindings.SequenceEqual(next.Bindings)) return true;
        try { next.Save(root); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        current = next;
        apply(next);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            Disconnect();
            endpoint.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private sealed class Endpoint : NativeWindow
    {
        private readonly GamePreferencesBridge owner;

        internal Endpoint(GamePreferencesBridge owner)
        {
            this.owner = owner;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg != ReadMessage && message.Msg != CopyDataMessage)
            {
                base.WndProc(ref message);
                return;
            }
            if (owner.viewport == IntPtr.Zero || message.WParam != owner.viewport) return;
            if (message.Msg == ReadMessage)
            {
                long field = message.LParam.ToInt64();
                if (field >= 0 && field <= Int32.MaxValue) message.Result = new IntPtr(owner.Read((int)field));
            }
            else if (owner.Save(message.LParam)) message.Result = new IntPtr(1);
        }
    }
}
