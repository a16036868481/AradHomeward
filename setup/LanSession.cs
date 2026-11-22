// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Dnf60Lifecycle;

internal static partial class Dnf60Control
{
    private sealed class LanGuestSession
    {
        internal readonly Process Worker, Game;
        internal readonly string Invite, Name;

        internal LanGuestSession(Process worker, Process game, string invite, string name)
        {
            Worker = worker;
            Game = game;
            Invite = invite;
            Name = name;
        }
    }

    private const string GuestAddress = "127.0.0.1";
    private static string GuestMarker { get { return Path.Combine(LogDir, "lan-guest-pid.txt"); } }
    private static LanGuestSession activeLanGuest;

    internal static bool LanGuestRunning()
    {
        try
        {
            int id;
            if (!File.Exists(GuestMarker) || !Int32.TryParse(File.ReadAllText(GuestMarker), out id))
                return false;
            using (var process = Process.GetProcessById(id))
                return !process.HasExited && ClientExitMonitor.IsInstalledClient(process.MainModule.FileName, Client);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (IOException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void ForegroundGame()
    {
        foreach (var process in Owned(Client))
            using (process)
                if (SafeName(process) == "dnf" && process.MainWindowHandle != IntPtr.Zero)
                    SetForegroundWindow(process.MainWindowHandle);
    }

    private static void ShowLanHost()
    {
        LanguageSupport.Load(Root);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!HasGameClient() || LanGuestRunning())
        {
            MessageBox.Show(LanRoomWindow.TextFor("lan_game_required"), LanRoomWindow.TextFor("lan_room"));
            return;
        }
        using (var window = new LanRoomWindow(LanHostRequest))
            Application.Run(window);
    }

    private static Dictionary<string,object> LanHostRequest(string action, Dictionary<string,object> data)
    {
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8787/api/lan/" + action);
        var serializer = new JavaScriptSerializer();
        request.Method = "POST";
        request.Proxy = null;
        request.Timeout = 12000;
        request.ReadWriteTimeout = 12000;
        request.ContentType = "application/json";
        request.Headers.Add("X-DNF60-Token", Convert.ToString(TrainerInfo()["token"]));
        byte[] payload = Encoding.UTF8.GetBytes(serializer.Serialize(data));
        request.ContentLength = payload.Length;
        using (var stream = request.GetRequestStream())
            stream.Write(payload, 0, payload.Length);
        try
        {
            using (var response = request.GetResponse())
            using (var input = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return serializer.Deserialize<Dictionary<string,object>>(input.ReadToEnd());
        }
        catch (WebException error)
        {
            if (error.Response == null)
                throw new InvalidOperationException("lan_open_failed");
            using (var response = error.Response)
            using (var input = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                throw new InvalidOperationException(Convert.ToString(serializer.Deserialize<Dictionary<string,object>>(input.ReadToEnd())["error"]));
        }
    }

    private static void CloseLanRoom()
    {
        if (!Open(8787)) return;
        try { LanHostRequest("close", new Dictionary<string,object>()); }
        catch (Exception error) { Log("LAN room close: " + error.Message); }
    }

    private static void WaitForLanGuest()
    {
        // The account window has closed. Keep the controller hidden until the
        // game exits normally or the LAN worker reports that it lost the room.
        while (activeLanGuest != null)
        {
            var session = activeLanGuest;
            activeLanGuest = null;
            using (session.Game)
            {
                string notice = null;
                using (session.Worker)
                    try { notice = session.Worker.StandardOutput.ReadLine(); }
                    catch (IOException) { }
                if (String.IsNullOrEmpty(notice) && session.Game.HasExited) continue;

                Result("lan-disconnect", false, LanRoomWindow.TextFor("lan_disconnected"));
                using (var reconnect = new LanRoomWindow(session.Name, session.Invite, session.Game))
                    reconnect.ShowDialog();
            }
        }
    }

    internal static async Task StartLanGuest(string invite, string name, Action<string> status, Process previousGame = null)
    {
        await Task.Run(() =>
        {
            using (ControlLocks.Shared.Operation())
            {
                if (previousGame != null && !previousGame.HasExited)
                {
                    if (!ClientExitMonitor.IsInstalledClient(previousGame.MainModule.FileName, Client))
                        throw new InvalidOperationException("lan_local_port_busy");
                    CloseGuestClient(previousGame);
                }
                if (HasGameClient() || Owned(Server).Length != 0)
                    throw new InvalidOperationException("lan_local_port_busy");
                status("lan_connecting");
                LanguageSupport.ValidateForGame(Root);
                string python = Path.Combine(Path.GetDirectoryName(PythonRuntime()), "python.exe");
                string script = Path.Combine(Root, "trainer", "lan_guest.py");
                var info = new ProcessStartInfo(python, "-B \"" + script + "\"")
                {
                    WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true,
                    RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8
                };
                var worker = Process.Start(info);
                if (worker == null) throw new InvalidOperationException("lan_unreachable");
                Process game = null;
                bool started = false;
                try
                {
                    WriteLanRequest(worker, new Dictionary<string,object> { { "invite", invite }, { "name", name } });
                    var credentials = ReadLanReply(worker, 20000);
                    ConfigureClientBridge(GuestAddress);
                    status("lan_starting");
                    game = LaunchProfileGame(Convert.ToString(credentials["account"]),
                                             Convert.ToString(credentials["secret"]), GuestAddress);
                    credentials.Clear();
                    WriteLanRequest(worker, new Dictionary<string,object> { { "pid", game.Id } });
                    ReadLanReply(worker, 6000);
                    worker.StandardInput.Close();
                    activeLanGuest = new LanGuestSession(worker, game, invite, name);
                    started = true;
                    Result("lan-join", true, "已加入局域网房间。");
                }
                finally
                {
                    if (!started)
                    {
                        using (worker)
                        using (game)
                            try
                            {
                                if (game != null) CloseGuestClient(game);
                            }
                            finally
                            {
                                worker.StandardInput.Close();
                                if (!worker.WaitForExit(2500)) worker.Kill();
                            }
                    }
                }
            }
        });
    }

    private static void CloseGuestClient(Process game)
    {
        if (game.HasExited) return;
        if (game.CloseMainWindow() && game.WaitForExit(2500)) return;
        // The old guest has lost its tunnels and cannot save anything to the
        // host. Some legacy dialogs refuse to handle the normal close message.
        try { if (!game.HasExited) game.Kill(); }
        catch (InvalidOperationException) { if (!game.HasExited) throw; }
        if (!game.WaitForExit(5000)) throw new InvalidOperationException("lan_close_old_game");
    }

    private static void WriteLanRequest(Process worker, Dictionary<string,object> data)
    {
        byte[] payload = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data) + "\n");
        var pipe = worker.StandardInput.BaseStream;
        pipe.Write(payload, 0, payload.Length);
        pipe.Flush();
    }

    private static Dictionary<string,object> ReadLanReply(Process worker, int milliseconds)
    {
        var read = worker.StandardOutput.ReadLineAsync();
        if (!read.Wait(milliseconds) || String.IsNullOrEmpty(read.Result))
            throw new InvalidOperationException("lan_unreachable");
        var reply = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(read.Result);
        if (!Convert.ToBoolean(reply["ok"]))
            throw new InvalidOperationException(Convert.ToString(reply["error"]));
        return reply;
    }
}
