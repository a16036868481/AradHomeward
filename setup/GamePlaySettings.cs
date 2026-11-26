// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal enum GameDisplayMode { Windowed, Maximized, FullScreen }

internal sealed class GamePlaySettings
{
    internal static readonly int[] WindowScales = { 100, 125, 150, 175, 200, 210, 225, 250, 275, 300, 320, 400, 450, 0 };
    internal static readonly Keys[] BindingChoices = new[] { Keys.None, Keys.Left, Keys.Right, Keys.Up, Keys.Down,
        Keys.Space, Keys.Enter, Keys.Escape, Keys.Tab, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown }
        .Concat(Enumerable.Range((int)Keys.A, 26).Select(key => (Keys)key))
        .Concat(Enumerable.Range((int)Keys.D0, 10).Select(key => (Keys)key))
        .Concat(Enumerable.Range((int)Keys.F1, 12).Select(key => (Keys)key)).ToArray();

    internal int WindowScale = 100;
    internal GameDisplayMode DisplayMode;
    internal bool GamepadEnabled = true;
    internal static readonly int[] TownSpeeds = { 100, 75, 50, 25 };
    internal static readonly int[] RepeatRates = { 4, 8, 12 };
    internal int TownSpeed = 100;
    internal bool RepeatEnabled;
    internal int RepeatRate = 8;
    internal Keys RepeatKey = Keys.X;

    internal static bool CanRepeat(Keys key)
    {
        return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9);
    }

    // Movement, then X/A/Y/B on each shoulder layer, then Start and Back.
    internal readonly Keys[] Bindings =
    {
        Keys.Left, Keys.Right, Keys.Up, Keys.Down,
        Keys.X, Keys.C, Keys.Z, Keys.Space,
        Keys.A, Keys.S, Keys.D, Keys.F,
        Keys.G, Keys.H, Keys.Q, Keys.W,
        Keys.E, Keys.R, Keys.T, Keys.Y,
        Keys.Escape, Keys.I
    };

    private static string SettingsPath(string root)
    {
        return Path.Combine(root, "setup", "gameplay.ini");
    }

    internal static GamePlaySettings Load(string root)
    {
        var settings = new GamePlaySettings();
        bool? repeatEnabled = null;
        int storedRate = 0;
        try
        {
            foreach (string line in File.ReadLines(SettingsPath(root)))
            {
                int equals = line.IndexOf('=');
                if (equals < 1) continue;
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                int scale;
                if (key == "WindowScale" && Int32.TryParse(value, out scale) && WindowScales.Contains(scale))
                    settings.WindowScale = scale;
                int mode;
                if (key == "DisplayMode" && Int32.TryParse(value, out mode) &&
                    mode >= (int)GameDisplayMode.Windowed && mode <= (int)GameDisplayMode.FullScreen)
                    settings.DisplayMode = (GameDisplayMode)mode;
                if (key == "GamepadEnabled" && (value == "0" || value == "1"))
                    settings.GamepadEnabled = value == "1";
                if (key == "GamepadKeys") settings.LoadBindings(value);
                int number;
                if (!Int32.TryParse(value, out number)) continue;
                if (key == "TownSpeed" && TownSpeeds.Contains(number)) settings.TownSpeed = number;
                if (key == "RepeatEnabled" && (number == 0 || number == 1)) repeatEnabled = number == 1;
                if (key == "RepeatRate" && (number == 0 || RepeatRates.Contains(number))) storedRate = number;
                if (key == "RepeatKey" && CanRepeat((Keys)number)) settings.RepeatKey = (Keys)number;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Earlier versions stored zero for off and a positive frequency for on.
        settings.RepeatEnabled = repeatEnabled ?? (storedRate != 0);
        if (storedRate != 0) settings.RepeatRate = storedRate;
        return settings;
    }

    private void LoadBindings(string value)
    {
        string[] keys = value.Split(',');
        if (keys.Length != Bindings.Length) return;
        for (int i = 0; i < keys.Length; i++)
        {
            int raw;
            if (Int32.TryParse(keys[i], out raw) && BindingChoices.Contains((Keys)raw))
                Bindings[i] = (Keys)raw;
        }
    }

    internal void Save(string root)
    {
        string file = SettingsPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string keys = String.Join(",", Bindings.Select(key => ((int)key).ToString(CultureInfo.InvariantCulture)));
        try
        {
            File.WriteAllLines(temporary, new[] { "[Gameplay]",
                "WindowScale=" + WindowScale.ToString(CultureInfo.InvariantCulture),
                "DisplayMode=" + ((int)DisplayMode).ToString(CultureInfo.InvariantCulture),
                "GamepadEnabled=" + (GamepadEnabled ? "1" : "0"), "GamepadKeys=" + keys,
                "TownSpeed=" + TownSpeed.ToString(CultureInfo.InvariantCulture),
                "RepeatEnabled=" + (RepeatEnabled ? "1" : "0"),
                "RepeatRate=" + RepeatRate.ToString(CultureInfo.InvariantCulture),
                "RepeatKey=" + ((int)RepeatKey).ToString(CultureInfo.InvariantCulture) }, Encoding.ASCII);
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
