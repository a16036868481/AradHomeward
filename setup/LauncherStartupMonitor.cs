// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Diagnostics;
using System.Threading;

internal static class LauncherStartupMonitor
{
    internal static string ReadyEvent(int pid){return "Local\\AradReturn_AccountPaneReady_"+pid;}
    internal static string Wait(Process launcher,Func<bool> hasGame,int timeoutMs,Action<string> log,string component="原版启动器")
    {
        using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,ReadyEvent(launcher.Id)))
        {
            var timer=Stopwatch.StartNew();
            while(true)
            {
                if(ready.WaitOne(0)){log("ACCOUNT PANE READY launcher="+launcher.Id);return "account-pane-ready";}
                if(hasGame()){log("GAME CLIENT DETECTED launcher="+launcher.Id);return "game-running";}
                if(launcher.HasExited)
                {
                    string code="0x"+unchecked((uint)launcher.ExitCode).ToString("X8");
                    log("ERROR original-launcher exited code="+code+" pid="+launcher.Id);
                    throw new InvalidOperationException(component+"在账号界面打开前退出（退出码 "+code+"）。\r\n请运行同目录的“启动检查.exe”，将“启动检查结果.json”发回排查。");
                }
                if(timer.ElapsedMilliseconds>=timeoutMs)
                {
                    log("ERROR original-launcher account-pane-timeout pid="+launcher.Id);
                    throw new InvalidOperationException(component+"仍在运行，但账号选择界面未能在规定时间内加载。\r\n请运行同目录的“启动检查.exe”，将检查结果及 logs\\startup-state.json 发回排查。");
                }
                ready.WaitOne(Math.Min(120,Math.Max(1,timeoutMs-(int)timer.ElapsedMilliseconds)));
            }
        }
    }
}
