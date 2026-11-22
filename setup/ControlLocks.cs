// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// A visible account pane is idle UI, not a running start/stop operation.
// Acquire an operation lease on the worker thread doing the synchronous work;
// never carry a Windows mutex across an await or hold it for a UI lifetime.
internal sealed class ControlLocks
{
    internal static readonly ControlLocks Shared=new ControlLocks("Local\\AradReturn_");
    private readonly string prefix,operationName;
    internal ControlLocks(string prefix){this.prefix=prefix;operationName=prefix+"Control_Local";}
    internal IDisposable Operation(){return Enter(operationName,"正在处理上一次操作，请稍候。",false);}
    internal IDisposable TryOperation(){return Enter(operationName,null,true);}
    internal IDisposable TryStartup(string root){return Enter(prefix+"Startup_"+Scope(root),null,true);}
    internal IDisposable AccountPane(string root,int originalLauncherPid)
    {
        // Only panes from the same installation and launcher instance contend.
        // An embedded pane can outlive a crashed original launcher briefly.
        string mode=originalLauncherPid==0?"_compat":"_embedded_"+originalLauncherPid;
        return Enter(prefix+"AccountPane_"+Scope(root)+mode,"账号选择窗口已经打开，请返回该窗口。",false);
    }
    private static string Scope(string root)
    {
        string path=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant();
        using(var sha=SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path)),0,16).Replace("-","");
    }
    private static IDisposable Enter(string name,string message,bool optional)
    {
        var mutex=new Mutex(false,name);bool held=false;
        try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}}
        catch{mutex.Dispose();throw;}
        if(held)return new Lease(mutex);
        mutex.Dispose();if(optional)return null;throw new InvalidOperationException(message);
    }
    private sealed class Lease:IDisposable
    {
        private Mutex mutex;
        internal Lease(Mutex value){mutex=value;}
        public void Dispose(){var owned=mutex;if(owned==null)return;mutex=null;try{owned.ReleaseMutex();}finally{owned.Dispose();}}
    }
}
