// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class LanRoomWindow : Form
{
    private sealed class NetworkChoice
    {
        internal string Address, Mask, Name;
        internal bool HasGateway;
        public override string ToString() { return Address + "  ·  " + Name; }
        internal Dictionary<string,object> Request()
        { return new Dictionary<string,object> { { "address", Address }, { "mask", Mask } }; }
    }

    private readonly Color field = Color.FromArgb(46, 50, 58), gold = Color.FromArgb(224, 182, 105);
    private readonly TextBox invite = new TextBox(), player = new TextBox();
    private readonly ComboBox addresses = new ComboBox();
    private readonly Label status = new Label(), heading = new Label(), guide = new Label();
    private readonly Label networkLabel = new Label(), endpoint = new Label(), guests = new Label();
    private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
    private readonly Button primary, copy = new Button();
    private readonly Timer refresh = new Timer { Interval = 3000 };
    private bool busy, opened;
    private string invitation = "";

    internal static string TextFor(string key) { return LanRoomText.Get(key, LanguageSupport.Current); }

    internal static Dictionary<string,object> PreferredNetwork()
    {
        var network = Networks().FirstOrDefault();
        if (network == null) throw new InvalidOperationException("lan_invalid_address");
        return network.Request();
    }

    private LanRoomWindow(bool hosting)
    {
        Text = TextFor(hosting ? "lan_room" : "lan_join");
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(29, 32, 38);
        ForeColor = Color.FromArgb(242, 238, 228);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = hosting ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(565, hosting ? 280 : 312);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = hosting;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 16), RowCount = hosting ? 7 : 8, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in hosting ? new[] { 37, 42, 22, 38, 27, 44 } : new[] { 36, 45, 23, 32, 23, 32, 44 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        heading.Dock = guide.Dock = DockStyle.Fill;
        heading.Text = TextFor(hosting ? "lan_wait" : "lan_join");
        guide.Text = TextFor(hosting ? "lan_select_network" : "lan_join_guide");
        heading.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(guide, 0, 1);
        if (hosting)
        {
            networkLabel.Dock = guests.Dock = DockStyle.Fill;
            networkLabel.Text = TextFor("lan_address");
            guests.ForeColor = Color.FromArgb(179, 191, 185);
            layout.Controls.Add(networkLabel, 0, 2);
            addresses.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleField(addresses, networkLabel.Text);
            addresses.Items.AddRange(Networks().ToArray());
            if (addresses.Items.Count > 0) addresses.SelectedIndex = 0;
            endpoint.Dock = DockStyle.Fill;
            endpoint.BackColor = field;
            endpoint.Padding = new Padding(8, 0, 6, 0);
            endpoint.TextAlign = ContentAlignment.MiddleLeft;
            endpoint.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
            endpoint.AccessibleName = TextFor("lan_room_address");
            endpoint.Visible = false;
            var networkField = new Panel { Dock = DockStyle.Fill };
            networkField.Controls.Add(addresses);
            networkField.Controls.Add(endpoint);
            layout.Controls.Add(networkField, 0, 3);
            layout.Controls.Add(guests, 0, 4);
        }
        else
        {
            layout.Controls.Add(LabelFor("lan_invite"), 0, 2);
            layout.Controls.Add(LabelFor("lan_name"), 0, 4);
            StyleField(invite, TextFor("lan_invite"));
            invite.MaxLength = 200;
            StyleField(player, TextFor("lan_name"));
            player.MaxLength = 24;
            layout.Controls.Add(invite, 0, 3);
            layout.Controls.Add(player, 0, 5);
        }
        actions.Dock = DockStyle.Fill;
        actions.FlowDirection = FlowDirection.RightToLeft;
        actions.WrapContents = false;
        primary = new Button();
        StyleButton(primary, hosting ? "lan_open" : "lan_connect_play", true);
        if (hosting)
        {
            StyleButton(copy, "lan_copy", true);
            actions.Controls.Add(copy);
        }
        actions.Controls.Add(primary);
        int actionRow = hosting ? 5 : 6;
        layout.Controls.Add(actions, 0, actionRow);
        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = true;
        layout.Controls.Add(status, 0, actionRow + 1);
        Controls.Add(layout);
        AcceptButton = primary;
        FormClosing += (s, e) => { if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        FormClosed += (s, e) => refresh.Dispose();
    }

    internal LanRoomWindow(string suggestedName, string previousInvite = "", Process previousGame = null) : this(false)
    {
        player.Text = suggestedName;
        invite.Text = previousInvite;
        if (previousGame != null)
        {
            Text = heading.Text = TextFor("lan_reconnect");
            guide.Text = TextFor("lan_disconnected");
            StyleButton(primary, "lan_reconnect", true);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
        }
        primary.Click += async (s, e) => await Work(async () =>
        {
            await Dnf60Control.StartLanGuest(invite.Text.Trim(), player.Text, SetStatus, previousGame);
            DialogResult = DialogResult.OK;
        });
    }

    internal LanRoomWindow(Func<string, Dictionary<string,object>, Dictionary<string,object>> request) : this(true)
    {
        Action<Dictionary<string,object>> apply = result =>
        {
            bool previous = opened;
            opened = Convert.ToBoolean(result["open"]);
            invitation = opened ? Convert.ToString(result["invite"]) : "";
            if (previous != opened) status.Text = "";
            heading.Text = TextFor(opened ? "lan_opened" : "lan_offline");
            heading.ForeColor = opened ? Color.FromArgb(161, 225, 180) : ForeColor;
            guide.Text = TextFor(opened ? "lan_host_guide" : "lan_select_network");
            networkLabel.Text = TextFor(opened ? "lan_room_address" : "lan_address");
            Uri room;
            endpoint.Text = opened && Uri.TryCreate(invitation, UriKind.Absolute, out room) ? room.Authority : "";
            guests.Text = opened ? String.Format(TextFor("lan_players"), result["guests"]) : "";
            addresses.Visible = !opened;
            endpoint.Visible = opened;
            copy.Visible = opened;
            StyleButton(primary, opened ? "lan_close" : "lan_open", !opened);
            AcceptButton = opened ? copy : primary;
            if (!opened && addresses.Items.Count == 0) SetStatus("lan_invalid_address");
        };
        primary.Click += async (s, e) =>
        {
            var choice = addresses.SelectedItem as NetworkChoice;
            if (!opened && choice == null) { SetStatus("lan_invalid_address"); return; }
            var data = opened ? new Dictionary<string,object>() : choice.Request();
            await Work(async () =>
            {
                SetStatus("lan_wait");
                apply(await Task.Run(() => request(opened ? "close" : "open", data)));
            });
        };
        copy.Click += (s, e) =>
        {
            try { Clipboard.SetText(invitation); SetStatus("lan_copied"); }
            catch (System.Runtime.InteropServices.ExternalException error) { SetStatus(error.Message); }
        };
        Func<Task> update = () => Work(async () => apply(await Task.Run(() => request("status", new Dictionary<string,object>()))));
        Shown += async (s, e) => { await update(); if (!IsDisposed) refresh.Start(); };
        refresh.Tick += async (s, e) =>
        {
            if (busy) return;
            if (!Dnf60Control.HasGameClient()) { Close(); return; }
            if (opened) await update();
        };
        copy.Visible = false;
        UpdateActions();
    }

    private static IEnumerable<NetworkChoice> Networks()
    {
        return NetworkChoices().OrderByDescending(n => n.HasGateway).ThenBy(n => n.Name).ThenBy(n => n.Address);
    }

    private static IEnumerable<NetworkChoice> NetworkChoices()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var properties = nic.GetIPProperties();
            bool gateway = properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork
                && !x.Address.Equals(System.Net.IPAddress.Any));
            foreach (var item in properties.UnicastAddresses)
            {
                if (item.Address.AddressFamily != AddressFamily.InterNetwork || item.IPv4Mask == null) continue;
                var a = item.Address.GetAddressBytes();
                if (a[0] != 10 && !(a[0] == 192 && a[1] == 168) && !(a[0] == 172 && a[1] >= 16 && a[1] <= 31)
                    && !(a[0] == 169 && a[1] == 254)) continue;
                yield return new NetworkChoice { Address = item.Address.ToString(), Mask = item.IPv4Mask.ToString(), Name = nic.Name, HasGateway = gateway };
            }
        }
    }

    private async Task Work(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        UpdateActions();
        try { await operation(); }
        catch (Exception error) { SetStatus(error.Message); }
        finally { busy = false; if (!IsDisposed) UpdateActions(); }
    }

    private void UpdateActions()
    {
        addresses.Enabled = !busy && !opened;
        primary.Enabled = invite.Enabled = player.Enabled = !busy;
        copy.Enabled = !busy && opened && !String.IsNullOrEmpty(invitation);
    }

    private void SetStatus(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => SetStatus(message))); return; }
        status.Text = TextFor(message);
    }

    private Label LabelFor(string key) { return new Label { Text = TextFor(key), Dock = DockStyle.Fill }; }

    private void StyleField(Control control, string label)
    {
        control.Dock = DockStyle.Top;
        control.AccessibleName = label;
        control.BackColor = field;
        control.ForeColor = ForeColor;
        if (control is TextBox) ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
    }

    private void StyleButton(Button button, string key, bool emphasize)
    {
        button.Text = TextFor(key);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = emphasize ? gold : field;
        button.ForeColor = emphasize ? BackColor : ForeColor;
        button.Size = new Size(145, 35);
    }
}
