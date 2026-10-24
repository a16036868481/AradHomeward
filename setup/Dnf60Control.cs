// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Dnf60Lifecycle;

[assembly: System.Reflection.AssemblyTitle("阿拉德·归途 · 会话服务")]
[assembly: System.Reflection.AssemblyCompany("eirros")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 eirros. All rights reserved.")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.23")]
[assembly: System.Reflection.AssemblyFileVersion("2.6.0.23")]

internal static class Dnf60Control
{
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    internal static readonly string Game = Path.Combine(Root, "DNF60_ACT12");
    internal static readonly string Server = Path.Combine(Game, "ACT12_Server");
    internal static readonly string Client = Path.Combine(Game, "ACT12");
    private static readonly string LogDir = Path.Combine(Root, "logs");
    private static int OriginalLauncherPid;
    private static bool StandaloneAccounts;
    internal const string KeepManagementEvent = "Local\\AradReturn_KeepManagement_Local";
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Launch(Path.Combine(Root,"归途修改器.exe"),"--manage",Root,false);
            return 0;
        }
        string action = args[0].TrimStart('-').ToLowerInvariant();
        if(action=="compat-accounts")
        {
            try{ShowStandaloneAccountPane();return 0;}
            catch(Exception ex){AccountStage("native-account-ui-failed",ex.Message,true);Result("compat-accounts",false,ex.Message);MessageBox.Show(ex.Message,"阿拉德·归途",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
        }
        if(action=="launcher-ui")
        {
            try { ShowOriginalAccountPane(args);return 0; }
            catch(Exception ex){AccountStage("launcher-ui-failed",ex.Message,true);Result("launcher-ui",false,ex.Message);MessageBox.Show(ex.Message,"阿拉德·归途",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
        }
        if(action=="background-host")
        {
            bool created;
            using(var instance=new Mutex(true,"Local\\AradReturn_Background_Local",out created))
            {if(!created)return 0;Application.Run(new ServerBackgroundContext());instance.ReleaseMutex();}
            return 0;
        }
        try { Run(action); return 0; }
        catch (Exception ex)
        {
            Log("ERROR " + action + " " + ex.Message);
            Result(action, false, ex.Message);
            if (action != "status" && action != "portable-check") MessageBox.Show(ex.Message, "阿拉德·归途", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    internal static void Run(string action)
    {
        if (action == "status") { Result(action, true, Status()); return; }
        if (action == "portable-check") { CheckPortableFiles(); return; }
        if (action == "launch-plan")
        {
            bool compatibility=LauncherMode.UseCompatibility(Root);
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir,"launch-plan.json"),new JavaScriptSerializer().Serialize(new {version=typeof(Dnf60Control).Assembly.GetName().Version.ToString(),root=Root,mode=compatibility?"compat":"original",usesOriginalLauncher=!compatibility,nativeAccountUi=compatibility,passwordEntry=false,processesStarted=false}),new UTF8Encoding(false));
            return;
        }
        if (action == "account-check")
        {
            var state=ProfileRequest(new Dictionary<string,object>{{"operation","list"}});
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir,"account-entry-check.json"),new JavaScriptSerializer().Serialize(state),new UTF8Encoding(false));
            return;
        }
        Process originalStarted=null,compatibleStarted=null;
        using (ControlLocks.Shared.Operation())
        {
            Log("BEGIN " + action);
            if (action == "server-only" || action == "gm") StartServices();
            if (action == "start")
            {
                if(HasGameClient())
                {StartServices();Launch(Path.Combine(Root,"归途修改器.exe"),"--profile-selected",Root,false);}
                else if(LauncherMode.UseCompatibility(Root))
                {
                    if(!ForegroundStandaloneAccounts())
                        compatibleStarted=Launch(Application.ExecutablePath,"--compat-accounts",Root,false);
                }
                else if(Owned(Client).Length==0)
                    originalStarted=Launch(Path.Combine(Client, "开始游戏.exe"), "", Client, false);
                else
                    foreach(var p in Owned(Client))try{if(p.MainWindowHandle!=IntPtr.Zero)SetForegroundWindow(p.MainWindowHandle);}catch{}
            }
            else if (action == "stop") StopAll();
            else if (action == "game-stop") { SetKeepManagement(true); StopGameOnly(); }
            else if (action == "management-restart") RestartManagement();
            else if (action == "gm") Launch(Path.Combine(Root, "归途修改器.exe"), "", Root, false);
            else if (action == "folder") Process.Start(new ProcessStartInfo(Game) { UseShellExecute = true });
            else if (action != "server-only") throw new ArgumentException("未知操作。");
            if(originalStarted==null&&compatibleStarted==null){Log("DONE " + action);Result(action, true, Status());}
        }
        if(originalStarted!=null)
            using(originalStarted)
            {
                string state=LauncherStartupMonitor.Wait(originalStarted,HasGameClient,25000,Log);
                Log("DONE start "+state);
                Result("start",true,state=="account-pane-ready"?"账号选择界面已显示，等待选择账号登录。":"游戏客户端已启动。");
            }
        if(compatibleStarted!=null)
            using(compatibleStarted)
            {
                string state=LauncherStartupMonitor.Wait(compatibleStarted,HasGameClient,25000,Log,"本机账号窗口");
                Log("DONE start compatibility "+state);
                Result("start",true,state=="account-pane-ready"?"账号选择界面已显示，等待选择账号登录。":"游戏客户端已启动。");
            }
    }

    private static bool ForegroundStandaloneAccounts()
    {
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath)))
            using(process)
            {
                try
                {
                    if(process.Id==Process.GetCurrentProcess().Id||process.HasExited||process.MainWindowHandle==IntPtr.Zero)continue;
                    if(!String.Equals(process.MainModule.FileName,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase))continue;
                    if(!process.MainWindowTitle.StartsWith("阿拉德·归途 ·",StringComparison.Ordinal))continue;
                    SetForegroundWindow(process.MainWindowHandle);return true;
                }
                catch(InvalidOperationException){}
                catch(System.ComponentModel.Win32Exception){}
            }
        return false;
    }

    private static void ShowStandaloneAccountPane()
    {
        StandaloneAccounts=true;
        if(ForegroundStandaloneAccounts())return;
        using(ControlLocks.Shared.AccountPane())
        using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,LauncherStartupMonitor.ReadyEvent(Process.GetCurrentProcess().Id)))
        {
            if(HasGameClient()){Launch(Path.Combine(Root,"归途修改器.exe"),"--profile-selected",Root,false);return;}
            AccountStage("native-account-ui-opening","正在打开本机账号选择窗口。");
            LocalAccountWindow.SetThreadDpiAwarenessContext(new IntPtr(-1));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LocalAccountWindow(IntPtr.Zero,0,ProfileRequest,StartProfileSession,
                ()=>{AccountStage("native-account-pane-visible","账号选择窗口已显示。");ready.Set();}));
        }
        AccountStage("native-account-pane-closed","账号选择窗口已关闭。");
    }

    private static Dictionary<string,object> ProfileRequest(Dictionary<string,object> request)
    {
        string python=Path.Combine(Path.GetDirectoryName(PythonRuntime()),"python.exe");
        var info=new ProcessStartInfo(python,"\""+Path.Combine(Root,"trainer","local_profiles.py")+"\"")
        {WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
         RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
         StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        var json=new JavaScriptSerializer {MaxJsonLength=1024*1024};
        using(var process=Process.Start(info))
        {
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            byte[] bytes=new UTF8Encoding(false).GetBytes(json.Serialize(request));
            process.StandardInput.BaseStream.Write(bytes,0,bytes.Length);process.StandardInput.Close();
            if(!process.WaitForExit(45000)){process.Kill();throw new InvalidOperationException("账号操作尚未完成，请检查本机存档后重试。");}
            Task.WaitAll(output,errors);
            Dictionary<string,object> response;
            try {response=(Dictionary<string,object>)json.DeserializeObject(output.Result);}
            catch {throw new InvalidOperationException("本机账号组件未能正常响应，请检查完整运行环境。");}
            if(!Convert.ToBoolean(response["ok"]))throw new InvalidOperationException(Convert.ToString(response["error"]));
            return (Dictionary<string,object>)response["data"];
        }
    }

    private static void ShowOriginalAccountPane(string[] args)
    {
        int pid;long handle;
        if(args.Length!=3||!Int32.TryParse(args[1],out pid)||!Int64.TryParse(args[2],out handle))throw new InvalidOperationException("原版启动器连接无效。");
        IntPtr parent=new IntPtr(handle);uint owner;
        GetWindowThreadProcessId(parent,out owner);
        using(var original=Process.GetProcessById(pid))
            if(owner!=(uint)pid||!String.Equals(original.MainModule.FileName,Path.Combine(Client,"开始游戏.exe"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("账号选择区只能嵌入当前游戏的原版启动器。");
        OriginalLauncherPid=pid;
        AccountStage("launcher-ui-attaching","正在加载原版启动器的账号选择区。");
        using(ControlLocks.Shared.AccountPane())
        using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,LauncherStartupMonitor.ReadyEvent(pid)))
        {
            if(HasGameClient()){Launch(Path.Combine(Root,"归途修改器.exe"),"--profile-selected",Root,false);PostMessage(parent,0x0010,IntPtr.Zero,IntPtr.Zero);return;}
            LocalAccountWindow.SetThreadDpiAwarenessContext(new IntPtr(-1));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LocalAccountWindow(parent,pid,ProfileRequest,StartProfileSession,()=>{AccountStage("account-pane-visible","账号选择窗口已显示。");ready.Set();}));
        }
        AccountStage("account-pane-closed","账号选择窗口已关闭。");
    }

    private static async Task StartProfileSession(string profileId,Action<string> state,Func<bool> stillOpen)
    {
        await Task.Run(()=>
        {
            using(ControlLocks.Shared.Operation())
            {
                bool servicesAlreadyRunning=BackendReady();
                try
                {
                    if(!stillOpen())throw new OperationCanceledException();
                    state("正在检查游戏语言包…");LanguageSupport.ValidateForGame(Root);
                    AccountStage("profile-services","正在准备本机游戏服务。");
                    state("正在准备本机游戏服务…");StartServices();
                    if(!stillOpen())throw new OperationCanceledException();
                    AccountStage("profile-authentication","本机服务就绪，正在载入所选账号。");
                    state("正在载入所选账号…");
                    var credentials=ProfileRequest(new Dictionary<string,object>{{"operation","login"},{"id",profileId}});
                    string account=Convert.ToString(credentials["account"]),secret=Convert.ToString(credentials["secret"]);
                    if(!stillOpen())throw new OperationCanceledException();
                    state("正在进入游戏…");
                    AccountStage("profile-game-start","正在启动游戏客户端。");
                    LaunchProfileGame(account,secret);
                    credentials.Clear();secret=null;
                    Launch(Path.Combine(Root,"归途修改器.exe"),"--profile-selected",Root,false);
                    Result("profile-login",true,"已登录所选本机账号");
                    AccountStage("profile-game-running","游戏客户端与修改器已启动。");
                }
                catch(Exception ex)
                {
                    AccountStage("profile-login-failed",ex.Message,true);Result("profile-login",false,ex.Message);
                    if(!servicesAlreadyRunning&&!HasGameClient())try{StopAll(true);}catch(Exception cleanup){Log("ERROR profile-cleanup "+cleanup.Message);}
                    throw;
                }
            }
        });
    }

    private static void LaunchProfileGame(string account,string secret)
    {
        string file=Path.Combine(Client,"DNF.exe");
        using(var game=Process.Start(new ProcessStartInfo(file,GameArguments(account,secret)){WorkingDirectory=Client,UseShellExecute=false,WindowStyle=ProcessWindowStyle.Normal}))
        {
            if(game==null)throw new InvalidOperationException("游戏启动未完成，请重试。");
            Log("START "+file+" pid="+game.Id);
            if(game.WaitForExit(1600)&&!HasGameClient())throw new InvalidOperationException("游戏启动后提前退出（退出码 0x"+unchecked((uint)game.ExitCode).ToString("X8")+"），请运行启动检查后将结果发回排查。");
        }
    }

    internal static string GameArguments(string account,string secret)
    {
        if(String.IsNullOrEmpty(account)||account.Length>11||account.Any(c=>c<'0'||c>'9')||String.IsNullOrEmpty(secret)||secret.Length>16||secret.Any(c=>!((c>='0'&&c<='9')||(c>='a'&&c<='z')||(c>='A'&&c<='Z'))))
            throw new InvalidOperationException("本机账号记录无效。");
        return "99?127.0.0.2?7001?"+account+"?"+secret+"?0?0?0?0?0?0?0?0?0?0?0";
    }

    private static void EnsureLocalIdentity()
    {
        string python=Path.Combine(Path.GetDirectoryName(PythonRuntime()),"python.exe");
        var info=new ProcessStartInfo(python,"-I -B \""+Path.Combine(Root,"trainer","first_run.py")+"\"")
        {WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true};
        using(var process=Process.Start(info))
        {
            var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(30000))throw new InvalidOperationException("Local login identity setup timed out.");
            if(process.ExitCode!=0)throw new InvalidOperationException("Local login identity setup failed. "+error.Result);
        }
    }

    private static void StartServices()
    {
        EnsureLocalIdentity();
        foreach (string file in new[] { Path.Combine(Server, "a12_compat_launcher.exe"), Path.Combine(Server, "a12_server.exe"), Path.Combine(Client, "开始游戏.exe"), Path.Combine(Server, "dnf_a12.sqlite") })
            if (!File.Exists(file)) throw new FileNotFoundException("缺少游戏文件：" + file);
        SetKeepManagement(false);
        ConfigureClientBridge();
        EnsureBackgroundHost();
        if (BackendReady()) { StartTrainer(); return; }
        var servers = Owned(Server).Where(p => SafeName(p) == "a12_server").ToArray();
        if (servers.Length > 0 && !Open(7001))
        {
            Thread.Sleep(1500);
            if (!Open(7001))
                foreach (var p in servers) Graceful(p, 15000, "请先关闭未启动的服务端窗口。");
        }
        if (!Owned(Server).Any(p => SafeName(p) == "a12_server"))
        {
            if (Open(7001)) throw new InvalidOperationException("7001 端口已被其他程序占用，未改动该程序。");
            StopOrphanRegistration();
            RelayPortPreflight.Ensure(Root);
            Launch(Path.Combine(Server, "a12_compat_launcher.exe"), "--all", Server, true);
        }
        var deadline = DateTime.UtcNow.AddSeconds(100);
        while (DateTime.UtcNow < deadline)
        {
            if (BackendReady()) { StartTrainer(); return; }
            Thread.Sleep(400);
        }
        throw new InvalidOperationException("服务端未在预定时间内就绪。请查看修改器“运行状态与日志”中的记录。");
    }

    private static void StopAll(bool keepLauncher=false)
    {
        // Finish a desktop edit before stopping the authoritative game service.
        string trainerExe = Path.Combine(Root, "归途修改器.exe");
        foreach (var p in Process.GetProcesses())
        {
            bool matches = false;
            try { matches = !p.HasExited && String.Equals(p.MainModule.FileName, trainerExe, StringComparison.OrdinalIgnoreCase); }
            catch { }
            if (matches) Graceful(p, 15000, "修改器正在完成操作，保存完成后会继续关闭。");
        }
        // This legacy client ignores WM_CLOSE. Offer a normal close first,
        // then stop the authoritative server before ending a residual client.
        foreach (var p in Owned(Client))
        {
            if(keepLauncher&&SafeName(p)=="开始游戏")continue;
            try { p.CloseMainWindow(); } catch (InvalidOperationException) { }
        }
        foreach (var p in Owned(Server).Where(p => SafeName(p) == "a12_server"))
            Graceful(p, 30000, "服务端尚未正常退出，为保护存档未强行结束它。请稍候重试。");
        StopTrainer();
        foreach (var p in Owned(Client))
        {
            if(keepLauncher&&SafeName(p)=="开始游戏")continue;
            if (p.WaitForExit(1500)) continue;
            if (IsInside(p, Client) && SafeName(p) == "dnf")
            {
                if (Owned(Server).Any(s => SafeName(s) == "a12_server"))
                    throw new InvalidOperationException("服务端仍在运行，未结束客户端。");
                Log("END residual DNF client after authoritative server exit pid=" + p.Id);
                p.Kill();
                if (!p.WaitForExit(8000)) throw new InvalidOperationException("客户端未退出。");
            }
            else Graceful(p, 8000, "游戏目录中还有启动工具未关闭，请关闭后重试。");
        }
        // The server normally shuts its login child down. Only terminate an orphan
        // backend after the game and the database-writing server have exited.
        foreach (var p in Owned(Server))
        {
            if (p.WaitForExit(6000)) continue;
            string name = SafeName(p);
            if (name == "a12_registration_server" || name == "a12_compat_launcher")
            {
                if (!IsInside(p, Server)) throw new InvalidOperationException("进程身份发生变化，已停止操作。");
                p.Kill();
                if (!p.WaitForExit(8000)) throw new InvalidOperationException("登录服务未退出。");
            }
            else throw new InvalidOperationException("游戏目录里还有工具在运行，请先关闭该工具再重试。");
        }
        if (Owned(Client).Any(p=>!keepLauncher||SafeName(p)!="开始游戏") || Owned(Server).Length != 0)
            throw new InvalidOperationException("还有游戏进程在运行，未生成关闭后的存档备份。");
        Backup();
        using(var quit=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\AradReturn_Background_Quit_Local"))quit.Set();
    }

    private static void SetKeepManagement(bool keep)
    {
        using(var mode=new EventWaitHandle(false,EventResetMode.ManualReset,KeepManagementEvent))
        { if(keep)mode.Set();else mode.Reset(); }
    }

    internal static bool HasGameClient()
    {
        bool found=false;
        foreach(var p in Process.GetProcessesByName("DNF"))
        {
            using(p)
            {
                try { if(!p.HasExited&&ClientExitMonitor.IsInstalledClient(p.MainModule.FileName,Client))found=true; }
                catch { }
            }
        }
        return found;
    }

    // Do not race a manual startup, a backup restore, or a newly opened game.
    // An occupied action mutex simply means the watcher should try again later.
    internal static bool TryStopAfterClientExit()
    {
        using(var operation=ControlLocks.Shared.TryOperation())
        using(var mode=new EventWaitHandle(false,EventResetMode.ManualReset,KeepManagementEvent))
        {
            if(operation==null||mode.WaitOne(0)||HasGameClient())return false;
            Log("BEGIN client-exit");
            StopAll();
            Log("DONE client-exit");
            Result("client-exit",true,Status());
            return true;
        }
    }

    internal static void AccountStage(string stage,string message,bool failed=false)
    {
        try
        {
            Log((failed?"ERROR ":"STATE ")+stage+" launcher="+OriginalLauncherPid+" "+message);
            var state=new {version=typeof(Dnf60Control).Assembly.GetName().Version.ToString(),stage=stage,failed=failed,message=message,mode=StandaloneAccounts?"compat":"original",originalLauncherPid=OriginalLauncherPid,controllerPid=Process.GetCurrentProcess().Id,time=DateTime.Now.ToString("o")};
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir,"startup-state.json"),new JavaScriptSerializer().Serialize(state),new UTF8Encoding(false));
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }

    internal static void WriteSessionState(string state,string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var data=new Dictionary<string,object>{{"version",typeof(Dnf60Control).Assembly.GetName().Version.ToString()},{"state",state},{"message",message},{"time",DateTime.Now.ToString("o")},{"hostPid",Process.GetCurrentProcess().Id},{"client",Path.Combine(Client,"DNF.exe")}};
            string file=Path.Combine(LogDir,"session-lifecycle.json"),temp=file+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(data),new UTF8Encoding(false));
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
        }
        catch(IOException ex){Log("SESSION STATE "+ex.Message);}
        catch(UnauthorizedAccessException ex){Log("SESSION STATE "+ex.Message);}
    }

    internal static void LogSessionError(Exception ex)
    { Log("ERROR client-exit "+ex.Message);Result("client-exit",false,ex.Message); }

    private static void ConfigureClientBridge()
    {
        string config = Path.Combine(Client, "config", "game.ini");
        string expected = "[network]\r\nhost=127.0.0.2\r\nport=7001\r\n";
        if (File.ReadAllText(config).Replace("\r\n", "\n") == expected.Replace("\r\n", "\n")) return;
        string backup = Path.Combine(Root, "setup", "game.ini.before-online-trainer");
        if (!File.Exists(backup)) File.Copy(config, backup, false);
        File.WriteAllText(config, expected, new UTF8Encoding(false));
    }

    private static void EnsureBackgroundHost()
    {
        using(var quit=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\AradReturn_Background_Quit_Local"))quit.Reset();
        bool existing;
        using(var probe=new Mutex(false,"Local\\AradReturn_Background_Local",out existing))
        {if(!existing){ServerBackgroundContext.HideInstalledWindows();return;}}
        Launch(Application.ExecutablePath,"--background-host",Root,true);
        Thread.Sleep(200);
        ServerBackgroundContext.HideInstalledWindows();
    }

    private static void StopGameOnly()
    {
        foreach(var p in Owned(Client)){try{p.CloseMainWindow();}catch(InvalidOperationException){}}
        foreach(var p in Owned(Server).Where(p=>SafeName(p)=="a12_server"))
            Graceful(p,30000,"服务端尚未正常退出，为保护存档未强行结束它。");
        foreach(var p in Owned(Client))
        {
            if(p.WaitForExit(1200))continue;
            if(IsInside(p,Client)&&SafeName(p)=="dnf"&&!Owned(Server).Any(s=>SafeName(s)=="a12_server"))
            {p.Kill();if(!p.WaitForExit(8000))throw new InvalidOperationException("客户端未退出。");}
            else Graceful(p,8000,"请先关闭游戏目录中的启动工具。");
        }
        // The game's normal shutdown can close its login child. Keep the local
        // registration/management backend available for offline backup restores.
        if(!Open(8080))
        {
            string login=Path.Combine(Server,"login_service");
            Launch(Path.Combine(login,"a12_registration_server.exe"),"--config \""+Path.Combine(login,"registration.json")+"\"",login,true);
            DateTime deadline=DateTime.UtcNow.AddSeconds(15);
            while(!Open(8080)&&DateTime.UtcNow<deadline)Thread.Sleep(200);
            if(!Open(8080))throw new InvalidOperationException("游戏已停止，但离线管理服务尚未就绪。");
        }
        var req=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:8080/api/backups/create");
        req.Method="POST";req.ContentType="application/json";req.Proxy=null;req.Timeout=30000;req.ReadWriteTimeout=30000;
        byte[] payload=Encoding.UTF8.GetBytes("{}");req.ContentLength=payload.Length;
        using(var stream=req.GetRequestStream())stream.Write(payload,0,payload.Length);
        using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))Log("OFFLINE MANAGEMENT BACKUP "+reader.ReadToEnd());
    }

    private static void StopOrphanRegistration()
    {
        if(Owned(Server).Any(p=>SafeName(p)=="a12_server"))throw new InvalidOperationException("游戏服务仍在运行，不能重启离线管理服务。");
        string expected=Path.Combine(Server,"login_service","a12_registration_server.exe");
        foreach(var p in Owned(Server).Where(p=>SafeName(p)=="a12_registration_server"))
        {
            if(!String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("登录进程身份不符。");
            p.Kill();if(!p.WaitForExit(8000))throw new InvalidOperationException("离线管理服务未退出。");
        }
    }

    private static void RestartManagement()
    {
        StopOrphanRegistration();string login=Path.Combine(Server,"login_service");
        Launch(Path.Combine(login,"a12_registration_server.exe"),"--config \""+Path.Combine(login,"registration.json")+"\"",login,true);
        DateTime deadline=DateTime.UtcNow.AddSeconds(15);while(!Open(8080)&&DateTime.UtcNow<deadline)Thread.Sleep(250);
        if(!Open(8080))throw new InvalidOperationException("离线管理服务未就绪。");
    }

    private static Dictionary<string, object> TrainerInfo()
    {
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8787/api/bootstrap");
        request.Timeout = 2500; request.ReadWriteTimeout = 2500; request.Proxy = null;
        using (var response = request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            object root;
            if (!result.TryGetValue("root", out root) || !String.Equals(Convert.ToString(root), Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("8787 端口的程序不是此目录的修改器，未改动它。");
            return result;
        }
    }

    private static bool TrainerReady()
    {
        if (!Open(8787)) return false;
        try
        {
            var info = TrainerInfo();
            var bridge = (Dictionary<string, object>)info["bridge"];
            return Convert.ToBoolean(bridge["ready"]);
        }
        catch { return false; }
    }

    private static void StartTrainer()
    {
        if (Open(8787)) { TrainerInfo(); }
        else
        {
            string python = PythonRuntime();
            string trainer = Path.Combine(Root, "trainer", "server.py");
            if (!File.Exists(python) || !File.Exists(trainer)) throw new FileNotFoundException("缺少本机修改器运行环境。");
            Launch(python, "\"" + trainer + "\"", Path.GetDirectoryName(trainer), true);
        }
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (TrainerReady()) return;
            Thread.Sleep(300);
        }
        throw new InvalidOperationException("在线修改器未就绪，请查看 "+Path.Combine(Root,"logs","trainer.log")+"。");
    }

    private static string PythonRuntime()
    {
        string bundled=Path.Combine(Root,"runtime","python","pythonw.exe");
        if(File.Exists(bundled))return bundled;
#if !PORTABLE
        if(File.Exists(@"C:\Python314\pythonw.exe"))return @"C:\Python314\pythonw.exe";
#endif
        throw new FileNotFoundException("缺少随包运行环境，请完整解压全部文件。",bundled);
    }

    private static void CheckPortableFiles()
    {
        string[] required={"归途修改器.exe","trainer\\server.py","trainer\\local_profiles.py","trainer\\catalog.json","trainer\\growth.json","runtime\\python\\python314._pth","runtime\\python\\python314.zip","DNF60_ACT12\\ACT12\\开始游戏.exe","DNF60_ACT12\\ACT12\\a12_profiles.dll","DNF60_ACT12\\ACT12_Server\\a12_server.exe","DNF60_ACT12\\ACT12_Server\\a12_compat_launcher.exe","DNF60_ACT12\\ACT12_Server\\dnf_a12.sqlite","DNF60_ACT12\\ACT12_Server\\login_service\\a12_registration_server.exe"};
        var missing=required.Where(p=>!File.Exists(Path.Combine(Root,p))).ToArray();
        string python=PythonRuntime();
        var report=new Dictionary<string,object>{{"root",Root},{"python",python},{"bundledPython",String.Equals(python,Path.Combine(Root,"runtime","python","pythonw.exe"),StringComparison.OrdinalIgnoreCase)},{"missing",missing},{"is64BitWindows",Environment.Is64BitOperatingSystem},{"version",typeof(Dnf60Control).Assembly.GetName().Version.ToString()}};
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir,"portable-check.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));
        if(missing.Length>0)throw new FileNotFoundException("便携包文件不完整："+String.Join("、",missing));
        if(!Environment.Is64BitOperatingSystem)throw new InvalidOperationException("本便携包需要 64 位 Windows。");
    }

    private static void StopTrainer()
    {
        if (!Open(8787)) return;
        var info = TrainerInfo();
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8787/api/shutdown");
        request.Method = "POST"; request.ContentType = "application/json"; request.Proxy = null;
        request.Timeout = 5000; request.ReadWriteTimeout = 5000;
        request.Headers.Add("X-DNF60-Token", Convert.ToString(info["token"]));
        byte[] payload = Encoding.UTF8.GetBytes("{}"); request.ContentLength = payload.Length;
        using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
        using (var response = request.GetResponse()) { }
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (!Open(8787)) return;
            Thread.Sleep(200);
        }
        throw new InvalidOperationException("修改器正在完成操作，请稍后再次关闭。");
    }

    private static void Graceful(Process process, int waitMs, string message)
    {
        try
        {
            if (process.HasExited) return;
            bool requested;
            if(SafeName(process)=="a12_server"&&IsInside(process,Server))requested=ServerBackgroundContext.CloseInstalledServer(process.Id);
            else if(String.Equals(process.MainModule.FileName,Path.Combine(Root,"归途修改器.exe"),StringComparison.OrdinalIgnoreCase))requested=ServerBackgroundContext.CloseInstalledTrainer(process.Id);
            else requested=process.CloseMainWindow();
            if (!requested || !process.WaitForExit(waitMs))
                if (!process.HasExited) throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException) { if (!process.HasExited) throw; }
    }

    private static void Backup()
    {
        string db = Path.Combine(Server, "dnf_a12.sqlite");
        if (!File.Exists(db)) return;
        string dest = Path.Combine(Root, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dest);
        foreach (string suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(db + suffix)) File.Copy(db + suffix, Path.Combine(dest, "dnf_a12.sqlite" + suffix), false);
        File.WriteAllText(Path.Combine(dest, "COMPLETE.txt"), "Game and server processes exited before this snapshot.\r\n" + DateTime.Now.ToString("o"), new UTF8Encoding(false));
        Log("BACKUP " + dest);
    }

    private static Process Launch(string file, string arguments, string directory, bool hidden)
    {
        var start = new ProcessStartInfo(file, arguments) { WorkingDirectory = directory, UseShellExecute = !hidden, CreateNoWindow = hidden, WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
        var p = Process.Start(start);
        Log("START " + file + " pid=" + (p == null ? "unknown" : p.Id.ToString()));
        if(p==null)throw new InvalidOperationException("未能创建程序进程："+Path.GetFileName(file));
        return p;
    }

    private static Process[] Owned(string directory)
    {
        return Process.GetProcesses().Where(p => IsInside(p, directory)).ToArray();
    }

    private static bool IsInside(Process p, string directory)
    {
        try { return !p.HasExited && Path.GetFullPath(p.MainModule.FileName).StartsWith(Path.GetFullPath(directory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string SafeName(Process p) { try { return p.ProcessName.ToLowerInvariant(); } catch { return ""; } }
    private static bool Open(int port)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var pending = client.BeginConnect("127.0.0.1", port, null, null);
                using (pending.AsyncWaitHandle)
                {
                    if (!pending.AsyncWaitHandle.WaitOne(180)) return false;
                    client.EndConnect(pending); return client.Connected;
                }
            }
        }
        catch { return false; }
    }

    private static bool BackendReady()
    {
        return Owned(Server).Any(p => SafeName(p) == "a12_server") && Open(7001) && Open(9999) && Open(8080);
    }

    private static bool Ready() { return BackendReady() && TrainerReady(); }

    internal static string Status()
    {
        return Ready() ? (Owned(Client).Length > 0 ? "游戏运行中 · 在线修改器已就绪" : "服务端及在线修改器已就绪") : (Owned(Server).Any(p=>SafeName(p)=="a12_server") ? "游戏服务运行中 · 修改器未就绪" : Open(8080)?"游戏已停止 · 离线管理可用":"游戏已关闭");
    }

    private static string Escape(string value) { return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"); }
    private static void Result(string action, bool ok, string message)
    {
        try
        {
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "control-result.json"), "{\"action\":\"" + Escape(action) + "\",\"ok\":" + (ok ? "true" : "false") + ",\"message\":\"" + Escape(message) + "\",\"time\":\"" + DateTime.Now.ToString("o") + "\"}", new UTF8Encoding(false));
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
    private static void Log(string message)
    {
        try
        {
        using(var writer=new Mutex(false,"Local\\AradReturn_ControlLog_Local"))
        {
        bool held=false;
        try
        {
        try{held=writer.WaitOne(1500);}catch(AbandonedMutexException){held=true;}
        if(!held)return;
        Directory.CreateDirectory(LogDir);
        File.AppendAllText(Path.Combine(LogDir, "control-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine, new UTF8Encoding(false));
        }
        finally{if(held)writer.ReleaseMutex();}
        }
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
}

// The original game server explicitly shows its window twice. Manage that
// exact product window for its full lifetime; never hide the client or dialogs.
internal sealed class ServerBackgroundContext : ApplicationContext
{
    private delegate bool EnumWindow(IntPtr h,IntPtr data);
    private delegate void WinEvent(IntPtr hook,uint evt,IntPtr h,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow fn,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder s,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr h,StringBuilder s,int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h,int state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent fn,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=700};
    private readonly WinEvent callback;
    private readonly EventWaitHandle quit=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\AradReturn_Background_Quit_Local");
    private readonly EventWaitHandle keepManagement=new EventWaitHandle(false,EventResetMode.ManualReset,Dnf60Control.KeepManagementEvent);
    private readonly ClientExitMonitor session=new ClientExitMonitor();
    private string lastSessionState;
    private int cleanupRunning;
    private DateTime retryAfter=DateTime.MinValue;
    private IntPtr hook;
    private DateTime lastRunning=DateTime.UtcNow;
    internal ServerBackgroundContext()
    {
        callback=(handle,evt,h,obj,child,thread,time)=>{if(obj==0&&child==0&&Installed(h,0))ShowWindow(h,0);};
        hook=SetWinEventHook(0x8002,0x8002,IntPtr.Zero,callback,0,0,0);
        quit.Reset();HideInstalledWindows();timer.Tick+=(s,e)=>Tick();timer.Start();
        Tick();
    }
    private void Tick()
    {
        if(quit.WaitOne(0)){if(Interlocked.CompareExchange(ref cleanupRunning,0,0)==0)ExitThread();return;}
        HideInstalledWindows();
        bool shouldClose=session.Observe(Dnf60Control.HasGameClient(),keepManagement.WaitOne(0),DateTime.UtcNow);
        if(Interlocked.CompareExchange(ref cleanupRunning,0,0)==0&&session.State!=lastSessionState)
        {lastSessionState=session.State;Dnf60Control.WriteSessionState(session.State,session.State=="armed"?"游戏退出后将自动保存并关闭全部关联组件。":session.State=="management-mode"?"当前保留离线管理，下一次启动游戏服务后恢复自动退出。":"等待本机游戏会话。");}
        if(shouldClose&&DateTime.UtcNow>=retryAfter&&Interlocked.CompareExchange(ref cleanupRunning,1,0)==0)
        {
            Dnf60Control.WriteSessionState("closing","检测到游戏已退出，正在保存并关闭关联组件。");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if(Dnf60Control.TryStopAfterClientExit())Dnf60Control.WriteSessionState("closed","游戏、修改器和后台服务已退出，存档备份已完成。");
                    else { lastSessionState=null;retryAfter=DateTime.UtcNow.AddSeconds(3); }
                }
                catch(Exception ex)
                {Dnf60Control.LogSessionError(ex);Dnf60Control.WriteSessionState("waiting-for-save",ex.Message);retryAfter=DateTime.UtcNow.AddSeconds(10);}
                finally { Interlocked.Exchange(ref cleanupRunning,0); }
            });
        }
        if(Process.GetProcessesByName("a12_server").Length>0||Process.GetProcessesByName("a12_registration_server").Length>0)lastRunning=DateTime.UtcNow;
        else if((DateTime.UtcNow-lastRunning).TotalSeconds>120&&Interlocked.CompareExchange(ref cleanupRunning,0,0)==0)ExitThread();
    }
    private static bool Installed(IntPtr h,int expectedPid)
    {
        var c=new StringBuilder(128);GetClassName(h,c,128);if(c.ToString()!="DNFServerUI")return false;
        uint pid;GetWindowThreadProcessId(h,out pid);if(expectedPid!=0&&pid!=expectedPid)return false;
        try{using(var p=Process.GetProcessById((int)pid))return String.Equals(p.MainModule.FileName,Path.Combine(Dnf60Control.Server,"a12_server.exe"),StringComparison.OrdinalIgnoreCase);}catch{return false;}
    }
    internal static void HideInstalledWindows(){EnumWindows((h,data)=>{if(IsWindowVisible(h)&&Installed(h,0))ShowWindow(h,0);return true;},IntPtr.Zero);}
    internal static bool CloseInstalledServer(int pid){bool sent=false;EnumWindows((h,data)=>{if(Installed(h,pid)){sent=PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero)||sent;}return true;},IntPtr.Zero);return sent;}
    internal static bool CloseInstalledTrainer(int pid)
    {
        bool sent=false;
        EnumWindows((h,data)=>
        {
            uint owner;GetWindowThreadProcessId(h,out owner);if(owner!=pid)return true;
            var title=new StringBuilder(256);GetWindowText(h,title,256);
            if(title.ToString()=="阿拉德·归途 · 修改器")
            { sent=PostMessage(h,0x8002,IntPtr.Zero,IntPtr.Zero)||sent;PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero); }
            else if(IsWindowVisible(h))
            {
                // Cancel an unanswered Yes/No prompt; never accept an edit just
                // because the game has ended. Other dialogs accept WM_CLOSE.
                var className=new StringBuilder(128);GetClassName(h,className,128);
                if(className.ToString()=="#32770")PostMessage(h,0x111,new IntPtr(7),IntPtr.Zero);
                sent=PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero)||sent;
            }
            return true;
        },IntPtr.Zero);
        return sent;
    }
    protected override void ExitThreadCore(){timer.Stop();timer.Dispose();quit.Dispose();keepManagement.Dispose();if(hook!=IntPtr.Zero)UnhookWinEvent(hook);base.ExitThreadCore();}
}

internal sealed class ControlWindow : Form
{
    private readonly Label status = new Label();
    private readonly Button[] controls = new Button[4];
    internal ControlWindow()
    {
        Text = "阿拉德·归途 · 管理";
        ClientSize = new Size(510, 302);
        Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 31, 44);
        ForeColor = Color.WhiteSmoke;
        var title = new Label { Text = "DNF · 60级怀旧", Location = new Point(26, 21), Size = new Size(450, 34), Font = new Font(Font.FontFamily, 19, FontStyle.Bold) };
        Controls.Add(title);
        status.SetBounds(28, 65, 450, 26); Controls.Add(status);
        string[] captions = { "启动游戏", "关闭游戏并备份", "打开桌面修改器", "打开游戏目录" };
        string[] actions = { "start", "stop", "gm", "folder" };
        for (int i = 0; i < controls.Length; i++)
        {
            int index = i;
            var button = new Button { Text = captions[i], FlatStyle = FlatStyle.Flat, BackColor = i == 0 ? Color.FromArgb(36, 111, 191) : Color.FromArgb(45, 57, 75), ForeColor = Color.White, Cursor = Cursors.Hand };
            button.SetBounds(28 + (i % 2) * 235, 107 + (i / 2) * 63, 222, 49);
            button.Click += delegate { Execute(actions[index]); };
            controls[i] = button; Controls.Add(button);
        }
        Controls.Add(new Label { Text = "选择保存的账号即可登录，新建账号只需名称。", Location = new Point(28, 245), Size = new Size(460, 28), ForeColor = Color.FromArgb(183, 201, 225) });
        var icon = Path.Combine(Dnf60Control.Client, "DNF.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        Shown += delegate { status.Text = Dnf60Control.Status(); };
    }
    private void Execute(string action)
    {
        foreach (var button in controls) button.Enabled = false;
        status.Text = action == "stop" ? "正在保存并关闭，请稍候…" : "正在准备，请稍候…";
        ThreadPool.QueueUserWorkItem(delegate
        {
            string error = null;
            try { Dnf60Control.Run(action); } catch (Exception ex) { error = ex.Message; }
            if (IsDisposed) return;
            BeginInvoke((Action)delegate
            {
                foreach (var button in controls) button.Enabled = true;
                status.Text = Dnf60Control.Status();
                if (error != null) MessageBox.Show(this, error, "阿拉德·归途", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        });
    }
}
