// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.Threading;

// A visible account pane is idle UI, not a running start/stop operation.
// Acquire an operation lease on the worker thread doing the synchronous work;
// never carry a Windows mutex across an await or hold it for a UI lifetime.
internal sealed class ControlLocks
{
    internal static readonly ControlLocks Shared=new ControlLocks("Local\\AradReturn_");
    private readonly string operationName,paneName;
    internal ControlLocks(string prefix){operationName=prefix+"Control_Local";paneName=prefix+"AccountPane_Local";}
    internal IDisposable Operation(){return Enter(operationName,"正在处理上一次操作，请稍候。",false);}
    internal IDisposable TryOperation(){return Enter(operationName,null,true);}
    internal IDisposable AccountPane(){return Enter(paneName,"账号选择窗口已经打开，请返回该窗口。",false);}
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
