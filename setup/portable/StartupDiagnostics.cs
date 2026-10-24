// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("阿拉德·归途 · 启动检查")]
[assembly: AssemblyCompany("eirros")]
[assembly: AssemblyCopyright("Copyright (c) 2026 eirros. All rights reserved.")]
[assembly: AssemblyVersion("2.5.2.0")]

internal static class StartupDiagnostics
{
    [DllImport("kernel32.dll")] private static extern uint GetACP();
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
    private static readonly List<object> Checks=new List<object>();
    private static string Root;
    private static bool Failed;
    private static readonly string[] Critical={
        "阿拉德·归途.exe", "DNF60控制台.exe", "归途修改器.exe",
        "runtime\\python\\python.exe", "runtime\\python\\pythonw.exe", "runtime\\python\\python314.dll", "runtime\\python\\python314.zip", "runtime\\python\\python314._pth",
        "trainer\\server.py", "trainer\\local_profiles.py", "trainer\\catalog.json", "trainer\\growth.json",
        "DNF60_ACT12\\ACT12\\DNF.exe", "DNF60_ACT12\\ACT12\\开始游戏.exe", "DNF60_ACT12\\ACT12\\netapi32.dll", "DNF60_ACT12\\ACT12\\a12_profiles.dll", "DNF60_ACT12\\ACT12\\a12_account_fix.dll",
        "DNF60_ACT12\\ACT12\\version.dll", "DNF60_ACT12\\ACT12\\Tenio\\TenBase.dll", "DNF60_ACT12\\ACT12\\Tenio\\TenFact.dll",
        "DNF60_ACT12\\ACT12\\Script.pvf", "DNF60_ACT12\\ACT12\\ui\\layout\\QQLogin.xml",
        "DNF60_ACT12\\ACT12_Server\\a12_server.exe", "DNF60_ACT12\\ACT12_Server\\a12_compat_launcher.exe", "DNF60_ACT12\\ACT12_Server\\dnf_a12.sqlite",
        "DNF60_ACT12\\ACT12_Server\\login_service\\a12_registration_server.exe", "DNF60_ACT12\\ACT12_Server\\login_service\\a12_sqlite3.dll",
        "DNF60_ACT12\\ACT12_Server\\login_service\\private\\registration.pfx", "DNF60_ACT12\\ACT12_Server\\login_service\\private\\certificate-password.txt"};

    [STAThread]
    private static int Main(string[] args)
    {
        Root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string report=null;
        try
        {
            if(!Directory.Exists(Path.Combine(Root,"DNF60_ACT12")))
                throw new InvalidOperationException("请把“启动检查.exe”放到游戏最外层，与“DNF60控制台.exe”放在一起，再运行。完整游戏文件夹不能留在 ZIP 内。");
            Check("windowsRequirements",()=>{
                using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;Int32.TryParse(Convert.ToString(key==null?null:key.GetValue("CurrentBuildNumber")),out build);
                    if(!Environment.Is64BitOperatingSystem||build<10240)throw new NotSupportedException("当前便携包需要 Windows 10/11 64 位，检测到 Windows build="+build+"，64位="+Environment.Is64BitOperatingSystem+"。");
                    return new {windowsBuild=build,is64BitWindows=true};
                }
            });
            Check("requiredFiles",()=>{
                var missing=Critical.Where(name=>!File.Exists(Path.Combine(Root,name))).ToArray();
                if(missing.Length>0)throw new FileNotFoundException("缺少文件："+String.Join("、",missing));
                return new {count=Critical.Length};
            });
            Check("writableGameFolder",()=>{
                string path=Path.Combine(Root,"logs","diagnostic-write-"+Guid.NewGuid().ToString("N")+".tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose))file.WriteByte(1);
                return new {writable=true};
            });
            Check("launcherMode",()=>{
                string path=Path.Combine(Root,"setup","launcher-mode.json");
                string mode="original";
                if(File.Exists(path))
                {
                    var settings=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));
                    object value;
                    mode=settings!=null&&settings.TryGetValue("mode",out value)?Convert.ToString(value):"";
                }
                if(mode!="compat"&&mode!="original")throw new InvalidDataException("启动方式配置无效。");
                return new{mode=mode,usesOriginalLauncher=mode=="original"};
            });
            Check("packageFiles",VerifyManifest);
            Check("bundledRuntime",()=>RunProbe());
            Check("loginRuntime",()=>RunProcess(Path.Combine(Root,"DNF60_ACT12","ACT12_Server","login_service","a12_registration_server.exe"),"--runtime-info",null,30000));
            var ports=IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Where(p=>new[]{7001,9999,8080,8787}.Contains(p.Port)).Select(p=>new{address=p.Address.ToString(),port=p.Port}).ToArray();
            var state=new Dictionary<string,object>();
            foreach(string name in new[]{"control-result.json","startup-state.json","launch-plan.json","portable-check.json","account-window-state.json","session-lifecycle.json"})
                try {string path=Path.Combine(Root,"logs",name);if(File.Exists(path))state[name]=Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8));}catch{}
            object system;
            using(var os=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            using(var framework=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                system=new {windows=Convert.ToString(os==null?null:os.GetValue("ProductName")),build=Convert.ToString(os==null?null:os.GetValue("CurrentBuildNumber")),displayVersion=Convert.ToString(os==null?null:os.GetValue("DisplayVersion")),is64BitWindows=Environment.Is64BitOperatingSystem,process64Bit=Environment.Is64BitProcess,ansiCodePage=GetACP(),frameworkRelease=Convert.ToString(framework==null?null:framework.GetValue("Release")),pathLength=Root.Length,rootContainsNonAscii=Root.Any(c=>c>127),root=Root};
            var result=new {time=DateTime.Now.ToString("o"),diagnosticsVersion="2.5.2",result=Failed?"failed":"component-checks-passed",system=system,checks=Checks,portsAlreadyListening=ports,recentState=state,recentApplicationErrors=ApplicationErrors(),gameStarted=false,servicesStarted=false,note="Only component checks. Game startup on this computer has not been verified."};
            // The report intentionally excludes passwords, account secrets, tokens and save contents.
            string output=Json.Serialize(result);
            try {report=Path.Combine(Root,"启动检查结果.json");File.WriteAllText(report,output,new UTF8Encoding(true));}
            catch {report=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"归途启动检查结果-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");File.WriteAllText(report,output,new UTF8Encoding(true));}
            if(!args.Contains("--quiet"))MessageBox.Show((Failed?"检测到启动组件异常。":"组件检查完成，仍需结合点击后的现象确认启动问题。")+"\r\n\r\n请把这个结果文件发给我：\r\n"+report,"阿拉德·归途 · 启动检查",MessageBoxButtons.OK,Failed?MessageBoxIcon.Warning:MessageBoxIcon.Information);
            return Failed?1:0;
        }
        catch(Exception error)
        {
            try {report=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"归途启动检查错误.txt");File.WriteAllText(report,error.ToString(),new UTF8Encoding(true));}catch{}
            if(!args.Contains("--quiet"))MessageBox.Show(error.Message+(report==null?"":"\r\n\r\n错误记录："+report),"阿拉德·归途 · 启动检查",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return 2;
        }
    }

    private static void Check(string name,Func<object> action)
    {try{Checks.Add(new{name=name,ok=true,detail=action()});}catch(Exception error){Failed=true;Checks.Add(new{name=name,ok=false,error=error.GetType().Name+": "+error.Message});}}

    private static object VerifyManifest()
    {
        string path=Path.Combine(Root,"setup","package-sha256.json");
        if(!File.Exists(path))throw new FileNotFoundException("缺少便携包完整性清单 setup\\package-sha256.json，请确认已完整解压。");
        var manifest=(Dictionary<string,object>)Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8));
        var missing=new List<string>();var changed=new List<string>();int count=0;int mutable=0;
        foreach(Dictionary<string,object> row in (object[])manifest["files"])
        {
            string relative=Convert.ToString(row["path"]),full=Path.GetFullPath(Path.Combine(Root,relative.Replace('/', '\\')));
            if(!full.StartsWith(Root+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("完整性清单路径无效。");
            // Saves, caches and settings legitimately change during normal play. Never hash credentials.
            string normalized=relative.ToLowerInvariant();
            string extension=Path.GetExtension(normalized);
            if(!new[]{".exe",".dll",".pyd",".py",".zip",".npk",".pvf",".xml",".xmlz",".ttf",".ttc",".png",".jpg",".jpeg",".bmp",".dat",".vfs"}.Contains(extension)||normalized.StartsWith("logs/")||normalized.StartsWith("backups/")||normalized.Contains("/icon-cache/")){mutable++;continue;}
            if(!File.Exists(full)){missing.Add(relative);continue;}
            string hash;
            using(var file=File.OpenRead(full))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();
            if(!String.Equals(hash,Convert.ToString(row["sha256"]),StringComparison.OrdinalIgnoreCase))changed.Add(relative);
            count++;
        }
        if(missing.Count>0||changed.Count>0)throw new InvalidDataException(Json.Serialize(new{missing=missing,changed=changed,checkedFiles=count}));
        return new{checkedFiles=count,mutableFilesSkipped=mutable};
    }

    private static object RunProbe()
    {
        string script;
        using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("startup_probe.py"))
        using(var reader=new StreamReader(resource,Encoding.UTF8))script=reader.ReadToEnd();
        return RunProcess(Path.Combine(Root,"runtime","python","python.exe"),"-",script,90000);
    }

    private static object ApplicationErrors()
    {
        var errors=new List<object>();
        try
        {
            using(var log=new EventLog("Application"))
            {
                for(int i=log.Entries.Count-1,iMin=Math.Max(0,log.Entries.Count-1000);i>=iMin;i--)
                {
                    var item=log.Entries[i];
                    if(item.TimeGenerated<DateTime.Now.AddHours(-6))break;
                    if((item.InstanceId&0xffff)!=1000)continue;
                    string message=item.Message;
                    if(!new[]{"DNF.exe","开始游戏.exe","DNF60控制台.exe","a12_server.exe","a12_registration_server.exe","python.exe","pythonw.exe"}.Any(name=>message.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0))continue;
                    // Application Error 1000 contains image/module paths and exception codes,
                    // unlike arbitrary application logs that may contain request data.
                    errors.Add(new {time=item.TimeGenerated.ToString("o"),source=item.Source,eventId=1000,message=message});
                    if(errors.Count==12)break;
                }
            }
            return new{available=true,events=errors};
        }
        catch(Exception error){return new{available=false,error=error.GetType().Name};}
    }

    private static object RunProcess(string file,string arguments,string input,int timeout)
    {
        var info=new ProcessStartInfo(file,arguments){WorkingDirectory=Path.GetDirectoryName(file),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        info.EnvironmentVariables["DNF60_DIAGNOSTIC_ROOT"]=Root;
        using(var process=Process.Start(info))
        {
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            if(input!=null){byte[] bytes=new UTF8Encoding(false).GetBytes(input);process.StandardInput.BaseStream.Write(bytes,0,bytes.Length);}
            process.StandardInput.Close();
            if(!process.WaitForExit(timeout)){process.Kill();throw new TimeoutException("运行环境检查超时："+Path.GetFileName(file));}
            Task.WaitAll(output,errors);
            string text=output.Result.Trim();
            if(process.ExitCode!=0)throw new InvalidOperationException("exit=0x"+unchecked((uint)process.ExitCode).ToString("X8")+" "+(text.Length>0?text:errors.Result.Trim()));
            try{return Json.DeserializeObject(text);}catch{return new{output=text};}
        }
    }
}
