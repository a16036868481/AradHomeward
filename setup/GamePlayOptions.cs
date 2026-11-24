// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal sealed class GamePlayOptions : Form
{
    private readonly string root;
    private readonly ComboBox windowSize = new ComboBox();
    private readonly CheckBox gamepad = new CheckBox();
    private readonly ComboBox[] bindings = new ComboBox[22];
    private readonly Panel gamepadOptions = new Panel();
    private static readonly Color Ink = Color.FromArgb(242, 238, 228);
    private static readonly Color Muted = Color.FromArgb(175, 181, 191);
    private static readonly Color Field = Color.FromArgb(46, 50, 58);
    private const int PaddingLeft = 26;

    internal GamePlayOptions(string installation)
    {
        root = installation;
        Text = T("play_options");
        Name = "AradHomewardPlayOptions";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MaximizeBox = MinimizeBox = false;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 493);
        BackColor = Color.FromArgb(29, 32, 38);
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = AddLabel(this, T("play_options"), PaddingLeft, 16, 510, 34, Ink);
        title.Font = new Font(Font.FontFamily, 17F, FontStyle.Bold);
        AddLabel(this, T("play_window_size"), PaddingLeft, 66, 178, 28, Ink);
        SetupList(windowSize, "GameWindowSize", T("play_window_size"));
        windowSize.SetBounds(204, 66, 390, 28);
        foreach (int scale in GamePlaySettings.WindowScales)
        {
            windowSize.Items.Add(scale == 0 ? T("play_fit_window") : scale == 100
                ? T("play_original_window")
                : String.Format("{0} × {1}  ({2}%)", GameWindow.OriginalWidth * scale / 100,
                    GameWindow.OriginalHeight * scale / 100, scale));
        }
        Controls.Add(windowSize);
        AddLabel(this, T("play_window_hint"), PaddingLeft, 101, 568, 30, Muted);

        gamepad.Name = "GamepadEnabled";
        gamepad.Text = gamepad.AccessibleName = T("play_gamepad");
        gamepad.SetBounds(PaddingLeft, 137, 568, 26);
        gamepad.CheckedChanged += (sender, args) => gamepadOptions.Enabled = gamepad.Checked;
        Controls.Add(gamepad);
        gamepadOptions.SetBounds(PaddingLeft, 168, 568, 240);
        Controls.Add(gamepadOptions);

        AddGridRow(0, T("play_movement"), new[] { "←", "→", "↑", "↓" }, -1);
        AddGridRow(1, "", null, 0);
        AddGridRow(2, T("play_buttons"), new[] { "X", "A", "Y", "B" }, -1);
        AddGridRow(3, T("play_direct"), null, 4);
        AddGridRow(4, "LB +", null, 8);
        AddGridRow(5, "RB +", null, 12);
        AddGridRow(6, "LB + RB +", null, 16);
        AddLabel(gamepadOptions, "Start", 0, 213, 141, 26, Muted);
        AddLabel(gamepadOptions, "Back", 287, 213, 66, 26, Muted);
        AddBinding(20, 143, 213, 119, "Start");
        AddBinding(21, 353, 213, 105, "Back");

        AddLabel(this, T("play_next_start"), PaddingLeft, 414, 568, 22, Muted);
        var reset = MakeButton("play_restore", PaddingLeft, 448, 170, false);
        var cancel = MakeButton("play_cancel", 363, 448, 106, false);
        var save = MakeButton("play_save", 479, 448, 115, true);
        reset.Click += (sender, args) => LoadFields(new GamePlaySettings());
        cancel.DialogResult = DialogResult.Cancel;
        save.Click += (sender, args) => SaveFields();
        CancelButton = cancel;
        AcceptButton = save;
        LoadFields(GamePlaySettings.Load(root));
        ResumeLayout(false);
    }

    private static string T(string key) { return LanRoomText.Get(key, LanguageSupport.Current); }

    private static Label AddLabel(Control owner, string value, int x, int y, int width, int height, Color color)
    {
        var label = new Label { Text = value, ForeColor = color, AutoEllipsis = true,
            UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
        label.SetBounds(x, y, width, height);
        owner.Controls.Add(label);
        return label;
    }

    private static void SetupList(ComboBox list, string name, string label)
    {
        list.Name = name;
        list.AccessibleName = label;
        list.DropDownStyle = ComboBoxStyle.DropDownList;
        list.BackColor = Field;
        list.ForeColor = Ink;
        list.FlatStyle = FlatStyle.Flat;
        list.MaxDropDownItems = 13;
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.DrawItem += DrawChoice;
    }

    private static void DrawChoice(object sender, DrawItemEventArgs args)
    {
        var list = (ComboBox)sender;
        bool selected = (args.State & DrawItemState.Selected) != 0;
        var background = selected ? Color.FromArgb(71, 67, 59) : Field;
        using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
        if (args.Index < 0) return;
        var text = Rectangle.Inflate(args.Bounds, -4, 0);
        TextRenderer.DrawText(args.Graphics, list.GetItemText(list.Items[args.Index]), args.Font,
            text, list.Enabled ? Ink : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        args.DrawFocusRectangle();
    }

    private void AddGridRow(int row, string label, string[] headers, int firstBinding)
    {
        int y = row * 30;
        if (label.Length != 0) AddLabel(gamepadOptions, label, 0, y, 141, 24, Muted);
        for (int column = 0; column < 4; column++)
        {
            int x = 143 + column * 107;
            if (headers != null) AddLabel(gamepadOptions, headers[column], x, y, 98, 24, Ink);
            else
            {
                string describedButton = firstBinding == 0 ? new[] { "←", "→", "↑", "↓" }[column] :
                    label + " " + new[] { "X", "A", "Y", "B" }[column];
                AddBinding(firstBinding + column, x, y, 102, describedButton);
            }
        }
    }

    private void AddBinding(int index, int x, int y, int width, string label)
    {
        var list = new ComboBox();
        SetupList(list, "GamepadBinding" + index, label);
        foreach (Keys choice in GamePlaySettings.BindingChoices) list.Items.Add(KeyLabel(choice));
        list.SetBounds(x, y, width, 24);
        gamepadOptions.Controls.Add(list);
        bindings[index] = list;
    }

    private static string KeyLabel(Keys key)
    {
        if (key == Keys.None) return T("play_unassigned");
        if (key == Keys.Left) return "←";
        if (key == Keys.Right) return "→";
        if (key == Keys.Up) return "↑";
        if (key == Keys.Down) return "↓";
        if (key == Keys.Escape) return "Esc";
        if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
        return key.ToString();
    }

    private Button MakeButton(string key, int x, int y, int width, bool primary)
    {
        var button = new Button { Name = key, Text = T(key), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Color.FromArgb(224, 182, 105) : Field,
            ForeColor = primary ? Color.FromArgb(30, 33, 38) : Ink };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(81, 84, 91);
        button.SetBounds(x, y, width, 34);
        Controls.Add(button);
        return button;
    }

    private void LoadFields(GamePlaySettings settings)
    {
        windowSize.SelectedIndex = Array.IndexOf(GamePlaySettings.WindowScales, settings.WindowScale);
        gamepad.Checked = settings.GamepadEnabled;
        for (int i = 0; i < bindings.Length; i++)
            bindings[i].SelectedIndex = Array.IndexOf(GamePlaySettings.BindingChoices, settings.Bindings[i]);
    }

    private void SaveFields()
    {
        var settings = new GamePlaySettings { WindowScale = GamePlaySettings.WindowScales[windowSize.SelectedIndex],
            GamepadEnabled = gamepad.Checked };
        for (int i = 0; i < bindings.Length; i++)
            settings.Bindings[i] = GamePlaySettings.BindingChoices[bindings[i].SelectedIndex];
        try
        {
            settings.Save(root);
            DialogResult = DialogResult.OK;
        }
        catch (IOException error) { ShowWriteError(error); }
        catch (UnauthorizedAccessException error) { ShowWriteError(error); }
    }

    private void ShowWriteError(Exception error)
    {
        MessageBox.Show(this, T("play_save_error") + Environment.NewLine + error.Message, Text,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
