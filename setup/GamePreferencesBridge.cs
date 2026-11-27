// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal sealed class GamePreferencesBridge : ApplicationContext
{
    private const string EndpointProperty = "AradHomeward.GamePreferences";
    private const string RepeatProperty = "AradHomeward.HoldRepeat";
    private const string TownProperty = "AradHomeward.TownSpeedPercent";
    private const int CopyDataMessage = 0x004a, Packet = 0x41524832, FirstBinding = 3, DefaultBinding = 0x100;
    private static readonly int ReadMessage = RegisterWindowMessage("AradHomeward.GamePreferences.v1");
    private readonly Process game;
    private readonly string root;
    private readonly string sceneEvent;
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenEvent(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal GamePreferencesBridge(Process game, string root, GamePlaySettings initial, Action<GamePlaySettings> apply)
    {
        this.game = game;
        this.root = root;
        string path = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        using (var sha = SHA256.Create())
            sceneEvent = "Local\\AradHomeward.Input." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path)),
                0, 12).Replace("-", "").ToLowerInvariant() + ".";
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
        if (window != viewport)
        {
            Disconnect();
            if (ReadMessage != 0 && window != IntPtr.Zero && SetProp(window, EndpointProperty, endpoint.Handle))
                viewport = window;
        }
        if (viewport != IntPtr.Zero) PublishInput();
    }

    private void Disconnect()
    {
        if (viewport != IntPtr.Zero && GetProp(viewport, EndpointProperty) == endpoint.Handle)
        {
            RemoveProp(viewport, RepeatProperty);
            RemoveProp(viewport, TownProperty);
            RemoveProp(viewport, EndpointProperty);
        }
        viewport = IntPtr.Zero;
    }

    private void PublishInput()
    {
        bool dungeon = InScene("dungeon");
        SetInputValue(RepeatProperty, dungeon && current.RepeatEnabled ? (current.RepeatRate << 8) | (int)current.RepeatKey : 0);
        int percent = !dungeon && InScene("town") ? GamePlaySettings.LoadTownSpeed(root, current.LegacyTownSpeed) :
            GamePlaySettings.FullTownSpeed;
        // Reserve zero for an absent property so a setting of 0% remains distinct.
        SetInputValue(TownProperty, percent < GamePlaySettings.FullTownSpeed ? percent + 1 : 0);
    }

    private bool InScene(string name)
    {
        IntPtr handle = OpenEvent(0x00100000, false, sceneEvent + name);
        if (handle == IntPtr.Zero) return false;
        CloseHandle(handle);
        return true;
    }

    private void SetInputValue(string name, int value)
    {
        if (GetProp(viewport, name) == new IntPtr(value)) return;
        if (value == 0) RemoveProp(viewport, name);
        else SetProp(viewport, name, new IntPtr(value));
    }

    private int Read(int field)
    {
        if (field == 0) return (int)current.DisplayMode + 1;
        if (field == 1) return current.WindowScale + 1;
        if (field == 2) return current.GamepadEnabled ? 2 : 1;
        if (field >= FirstBinding && field < FirstBinding + current.Bindings.Length)
            return (int)current.Bindings[field - FirstBinding] + 1;
        int travel = FirstBinding + current.Bindings.Length;
        if (field == travel) return current.LegacyTownSpeed + 1;
        if (field == travel + 1) return current.RepeatEnabled ? current.RepeatRate + 1 : 1;
        if (field == travel + 2) return (int)current.RepeatKey + 1;
        if (field == travel + 3) return current.RepeatEnabled ? 2 : 1;
        if (field == travel + 4) return current.RepeatRate + 1;
        if (field >= DefaultBinding && field < DefaultBinding + defaults.Bindings.Length)
            return (int)defaults.Bindings[field - DefaultBinding] + 1;
        return 0;
    }

    private bool Save(IntPtr message)
    {
        if (message == IntPtr.Zero) return false;
        var data = (CopyData)Marshal.PtrToStructure(message, typeof(CopyData));
        var next = new GamePlaySettings();
        int travel = FirstBinding + next.Bindings.Length;
        bool hasRepeatSwitch = data.Length == (travel + 4) * sizeof(int);
        bool extended = hasRepeatSwitch || data.Length == (travel + 3) * sizeof(int);
        if (data.Tag != new IntPtr(Packet) || (!extended && data.Length != travel * sizeof(int)) || data.Data == IntPtr.Zero)
            return false;
        int length = data.Length / sizeof(int);
        var values = new int[length];
        Marshal.Copy(data.Data, values, 0, length);
        if (!Enum.IsDefined(typeof(GameDisplayMode), values[0]) || !GamePlaySettings.WindowScales.Contains(values[1]) ||
            values[2] < 0 || values[2] > 1) return false;
        next.DisplayMode = (GameDisplayMode)values[0];
        next.WindowScale = values[1];
        next.GamepadEnabled = values[2] != 0;
        int rate = extended ? values[travel + 1] : 0;
        int enabled = hasRepeatSwitch ? values[travel + 3] : (rate == 0 ? 0 : 1);
        if (enabled < 0 || enabled > 1) return false;
        next.LegacyTownSpeed = current.LegacyTownSpeed;
        next.RepeatRate = hasRepeatSwitch || rate != 0 ? rate : current.RepeatRate;
        next.RepeatEnabled = extended ? enabled == 1 : current.RepeatEnabled;
        next.RepeatKey = extended ? (Keys)values[travel + 2] : current.RepeatKey;
        if (!GamePlaySettings.RepeatRates.Contains(next.RepeatRate) ||
            !GamePlaySettings.CanRepeat(next.RepeatKey)) return false;
        for (int i = 0; i < next.Bindings.Length; i++)
        {
            var key = (Keys)values[FirstBinding + i];
            if (!GamePlaySettings.BindingChoices.Contains(key)) return false;
            next.Bindings[i] = key;
        }
        if (current.DisplayMode == next.DisplayMode && current.WindowScale == next.WindowScale &&
            current.RepeatEnabled == next.RepeatEnabled &&
            current.RepeatRate == next.RepeatRate && current.RepeatKey == next.RepeatKey &&
            current.GamepadEnabled == next.GamepadEnabled && current.Bindings.SequenceEqual(next.Bindings)) return true;
        try { next.Save(root); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        current = next;
        PublishInput();
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
