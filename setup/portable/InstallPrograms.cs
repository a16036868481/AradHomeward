// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class InstallPrograms
{
#if PORTABLE
    internal const string Controller = "AradHomeward.exe";
    internal const string Trainer = "AradTrainer.exe";
    internal const string LegacyLauncher = "LegacyLauncher.exe";
    internal const string Diagnostics = "CheckStartup.exe";
#else
    internal const string Controller = "DNF60控制台.exe";
    internal const string Trainer = "归途修改器.exe";
    internal const string LegacyLauncher = "开始游戏.exe";
    internal const string Diagnostics = "启动检查.exe";
#endif
#if WINDOWS7_RUNTIME
    internal const string PythonTag = "python38";
    private const int MinimumWindowsBuild = 7601;
    private const string WindowsRequirement = "Windows 7 SP1 或更新版本";
#else
    internal const string PythonTag = "python314";
    private const int MinimumWindowsBuild = 14393;
    private const string WindowsRequirement = "Windows 10 1607 或更新版本";
#endif

    internal static int VerifyWindows()
    {
        int build;
        using (var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            if (!Int32.TryParse(Convert.ToString(version == null ? null : version.GetValue("CurrentBuildNumber")), out build))
                build = Environment.OSVersion.Version.Build;
        if (!Environment.Is64BitOperatingSystem || build < MinimumWindowsBuild)
            throw new NotSupportedException("此版本需要 " + WindowsRequirement + "（64 位）。当前系统 build=" + build + "，64 位=" + Environment.Is64BitOperatingSystem + "。");
        return build;
    }

    internal static void EnsureLocalIdentity(string root, string python)
    {
        string script = Path.Combine(root, "trainer", "first_run.py");
        string arguments = "-I -B \"" + script + "\"";
#if WINDOWS7_RUNTIME
        arguments += " --legacy-pfx";
#endif
        var info = new ProcessStartInfo(python, arguments)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8
        };
        info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
        using (var process = Process.Start(info))
        {
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(65000))
            {
                process.Kill(); process.WaitForExit();
                throw new InvalidOperationException("[LOGIN-SETUP-TIMEOUT] 本机证书或存档准备超时，请等其他启动操作结束后重试。");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Local login identity setup failed. " + error.Result);
        }
    }

    internal static Dictionary<string,object> LoginCommand(string root,string python,string command)
    {
        var script=Path.Combine(root,"trainer","login_service.py");
        var info=new ProcessStartInfo(python,"-I -B \""+script+"\" "+command)
        {
            WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8
        };
        using(var child=Process.Start(info))
        {
            var output=child.StandardOutput.ReadToEndAsync();
            var error=child.StandardError.ReadToEndAsync();
            // The helper owns its launched backend and cleans up each failed attempt.
            // Its bounded waits include first .NET extraction, HTTP readiness and bind-race retries.
            if(!child.WaitForExit(command=="ensure"?400000:command=="backup"?45000:15000))
            {
                child.Kill();child.WaitForExit();
                throw new TimeoutException("[LOGIN-HELPER-TIMEOUT] 本机登录组件未响应，请运行 CheckStartup.exe。");
            }
            string text=output.Result;
            error.Wait();
            Dictionary<string,object> result;
            try {result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);}
            catch(ArgumentException){result=null;}
            if(result==null||!result.ContainsKey("ok"))
                throw new InvalidOperationException("[LOGIN-HELPER] 登录检查组件未能运行，请完整解压游戏包。");
            if(child.ExitCode!=0||!Convert.ToBoolean(result["ok"]))
                throw new InvalidOperationException("["+Convert.ToString(result["code"])+"] "+Convert.ToString(result["message"]));
            return result;
        }
    }
}
